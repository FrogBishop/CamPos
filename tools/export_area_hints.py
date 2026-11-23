"""从公开的原神场景配置生成 Minecraft Areas Hint 区域文件。仅使用 Python 标准库。"""
import argparse
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor
import hashlib
from http.client import HTTPException
import json
import math
import os
from pathlib import Path
import re
import sys
import tempfile
import time
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import Request, urlopen
import zipfile


REPO = "DimbreathBot/AnimeGameData"
API = f"https://api.github.com/repos/{REPO}"
RAW = f"https://raw.githubusercontent.com/{REPO}"
WORLD_DIR = "BinOutput/Scene/WorldArea"
CONFIG = "ExcelBinOutput/WorldAreaConfigData.json"
SCENE_FILE = re.compile(r"scene(\d+)_worldArea\.json")
DEFAULT_OUTPUT = Path(__file__).resolve().parents[1] / "exports"
CHUNK = 1024 * 1024


def request(url, byte_range=None):
    headers = {"User-Agent": "CamPos-Areas-Hint-Exporter"}
    if byte_range:
        start, end, size = byte_range
        headers["Range"] = f"bytes={start}-{end}"
        url += f"?part={start}"
    for attempt in range(3):
        try:
            with urlopen(Request(url, headers=headers), timeout=45) as response:
                if byte_range and response.status == 206:
                    if response.headers.get("Content-Range") != f"bytes {start}-{end}/{size}":
                        raise ValueError("上游返回了不匹配的文件分段")
                    data = response.read()
                    if len(data) != end - start + 1:
                        raise OSError("下载的文件分段不完整")
                    return data
                if byte_range and start != 0:
                    raise ValueError("上游没有按请求返回文件分段")
                return response.read()
        except HTTPError as exc:
            exc.close()
            if exc.code not in (408, 429, 500, 502, 503, 504) or attempt == 2:
                raise OSError(f"HTTP {exc.code} {exc.reason} ({url})") from exc
        except (OSError, HTTPException) as exc:
            if attempt == 2:
                raise OSError(f"{exc} ({url})") from exc
        print(f"网络请求中断，将重试 {url}", file=sys.stderr, flush=True)
        time.sleep(2 ** attempt)


def snapshot(ref):
    # 列表接口只返回提交信息，避免为大型数据更新生成整份 diff。
    recent = json.loads(request(f"{API}/commits?sha={quote(ref, safe='')}&per_page=1"))
    if not recent:
        raise ValueError(f"上游版本没有提交 {ref}")
    commit = recent[0]
    sha = commit["sha"]
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError("上游提交标识无效")

    def contents(path):
        data = json.loads(request(f"{API}/contents/{path}?ref={sha}"))
        if not isinstance(data, list) or len(data) < 1000:
            return data
        # Contents API 的目录响应最多 1000 项，更大的目录从固定提交的树读取。
        tree_sha = sha
        if path:
            parent, _, name = path.rpartition("/")
            children = {entry["name"]: entry["sha"] for entry in contents(parent)}
            tree_sha = children[name]
        tree = json.loads(request(f"{API}/git/trees/{tree_sha}"))
        if tree.get("truncated"):
            raise ValueError(f"GitHub 返回的目录树不完整 {path}")
        return [{**entry, "name": entry["path"], "path": "/".join(filter(None, (path, entry["path"])))}
                for entry in tree["tree"]]

    with ThreadPoolExecutor(max_workers=3) as pool:
        config, world, languages = pool.map(contents, (CONFIG, WORLD_DIR, "TextMap"))
    shapes = sorted((f for f in world if SCENE_FILE.fullmatch(f["name"])), key=lambda f: f["name"])
    if not shapes:
        raise ValueError(f"在 {WORLD_DIR} 没有找到场景边界文件")
    # 优先使用存放区域名称哈希的 Medium 表，较早的版本可能只有通用表。
    for stem in ("TextMap_MediumCHS", "TextMapCHS"):
        texts = sorted((f for f in languages if re.fullmatch(stem + r"(?:_\d+)?\.json", f["name"])),
                       key=lambda f: f["name"])
        if texts:
            break
    if not texts:
        raise ValueError("在该版本找不到简体中文文本表")
    version = commit["commit"]["message"].splitlines()[0]
    return sha, version, [config, *shapes, *texts]


def git_blob(data):
    return hashlib.sha1(f"blob {len(data)}\0".encode("ascii") + data).hexdigest()


def cached_file(cache, commit, entry):
    path, size, sha = entry["path"], entry["size"], entry["sha"]
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError(f"无效的 Git 文件标识 {path}")
    target = cache / sha
    if target.is_file() and target.stat().st_size == size and git_blob(target.read_bytes()) == sha:
        return target

    url = f"{RAW}/{commit}/{quote(path)}"
    print(f"下载 {path}  ({size:,} 字节)", flush=True)
    if size <= 4 * CHUNK:
        data = request(url)
    else:
        def part(start):
            return request(url, (start, min(start + CHUNK, size) - 1, size))

        first = part(0)
        if len(first) == size:  # 某些网络直接返回整个文件。
            data = first
        else:
            with ThreadPoolExecutor(max_workers=6) as pool:
                data = b"".join([first, *pool.map(part, range(CHUNK, size, CHUNK))])
    if len(data) != size or git_blob(data) != sha:
        raise ValueError(f"文件大小或 Git blob 校验失败 {path}")

    with tempfile.TemporaryDirectory(dir=cache) as tmp:
        temporary = Path(tmp) / "blob"
        temporary.write_bytes(data)
        temporary.replace(target)
    return target


def read_json(path):
    return json.loads(path.read_bytes())


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as f:
        json.dump(value, f, ensure_ascii=False, indent=2, allow_nan=False)
        f.write("\n")


def collect_areas(files):
    names = {}
    for path, file in files.items():
        if path.startswith("TextMap/"):
            names.update(read_json(file))
    areas = {}
    configs = read_json(files[CONFIG])
    for row in configs:
        level = {"LEVEL_1": 1, "LEVEL_2": 2}[row["areaType"]]
        key = (row["sceneID"], row["areaID1"], row.get("areaID2", 0))
        if key in areas:
            raise ValueError(f"区域配置键重复 {key}")
        areas[key] = {
            "sceneId": key[0], "configId": row["id"], "level": level,
            "areaID1": key[1], "areaID2": row.get("areaID2"),
            "areaNameTextMapHash": row["areaNameTextMapHash"],
            "nameCHS": names.get(str(row["areaNameTextMapHash"])),
            "showTips": row.get("showTips"), "shapes": [],
        }
        if "areaOffset" in row:
            areas[key]["areaOffset"] = row["areaOffset"]
    del names

    unmatched = []
    for path, file in files.items():
        match = SCENE_FILE.fullmatch(path.rsplit("/", 1)[-1])
        if not match:
            continue
        scene = int(match[1])
        document = read_json(file)
        if document in ({}, []):
            continue
        for gi, group in enumerate(document["level1Areas"]):
            entries = [("level1Area", group["level1Area"])]
            entries.extend((f"level2Areas/{i}", a) for i, a in enumerate(group.get("level2Areas", [])))
            for subpath, entry in entries:
                key = (scene, entry["id1"], entry.get("id2", 0))
                shape = {"source": path, "jsonPointer": f"/level1Areas/{gi}/{subpath}/polygonData",
                         **entry["polygonData"]}
                if key in areas:
                    areas[key]["shapes"].append(shape)
                else:
                    unmatched.append({"sceneId": scene, "id1": key[1], "id2": key[2], "shape": shape})
    return [areas[key] for key in sorted(areas)], unmatched


def mc_geometry(shape):
    vertices = [{"x": -v.get("z", 0), "z": -v.get("x", 0)} for v in shape["roadPoints"]]
    ymin = shape["minArea"].get("y", 0)
    ymax = shape["maxArea"].get("y", 0)
    if len(vertices) < 3 or ymin > ymax or not all(math.isfinite(v) for v in (ymin, ymax)):
        raise ValueError(f"轮廓或高度无效 {shape['source']} {shape['jsonPointer']}")
    xs = [p["x"] for p in vertices]
    zs = [p["z"] for p in vertices]
    if not all(map(math.isfinite, xs + zs)):
        raise ValueError(f"顶点不是有限坐标 {shape['source']} {shape['jsonPointer']}")
    xmin, xmax, zmin, zmax = min(xs), max(xs), min(zs), max(zs)
    box = [{"x": xmin, "z": zmax}, {"x": xmax, "z": zmax},
           {"x": xmax, "z": zmin}, {"x": xmin, "z": zmin}]
    return vertices, box, {"min": ymin, "max": ymax}


def title(area):
    return area["nameCHS"] or f"区域{area['configId']}"


def unique_name(preferred, used):
    result = preferred
    index = 2
    while result in used:
        result = f"{preferred}〔{index}〕"
        index += 1
    used.add(result)
    return result


def make_profiles(areas):
    counts = Counter()
    for area in areas:
        counts[area["sceneId"], title(area)] += len(area["shapes"])
    names = defaultdict(set)
    for area in areas:
        scene, cid = area["sceneId"], area["configId"]
        for i, shape in enumerate(area["shapes"], 1):
            label = title(area)
            if counts[scene, label] > 1:
                suffix = str(cid) if len(area["shapes"]) == 1 else f"{cid}-{i}"
                label += f"〔{suffix}〕"
            shape["minecraft"] = {"name": unique_name(label, names[scene]),
                                  "surfacename": title(area), "entries": []}

    files = defaultdict(list)
    for profile in ("all", "tips-only"):
        included = [a for a in areas if profile == "all" or a["showTips"] is True]
        parent_names = {(a["sceneId"], a["areaID1"]): [s["minecraft"]["name"] for s in a["shapes"]]
                        for a in included if a["level"] == 1}
        used = {s: set(values) for s, values in names.items()}
        # 先确定实际导出的父级，再按源顺序写入，保持重叠区域的检测顺序。
        for area in included:
            scene = area["sceneId"]
            parent_key = (scene, area["areaID1"])
            rel = f"{profile}/scene-{scene}/overworld.json"
            for shape in area["shapes"]:
                mc = shape["minecraft"]
                vertices, box, altitude = mc_geometry(shape)
                parents = parent_names.get(parent_key, []) if area["level"] == 2 else []
                # 模组每条记录只能指向一个父名称。父级若有多个独立轮廓，
                # 每个父轮廓下各注册一次；缺少父轮廓时则让子区域独立参与检测。
                for i, parent in enumerate(parents or [None], 1):
                    name = mc["name"] if i == 1 else unique_name(f"{mc['name']}〔父{i}〕", used[scene])
                    level = 1 if parent is None else 2
                    files[rel].append({
                        "name": name, "vertices": vertices, "second-vertices": box,
                        "altitude": altitude, "level": level, "base-name": parent,
                        "surfacename": mc["surfacename"], "color": "#FFFFFF",
                    })
                    mc["entries"].append({"profile": profile, "file": rel, "name": name,
                                          "level": level, "base-name": parent})
    for objects in files.values():
        objects.sort(key=lambda obj: obj["level"])
        levels = {obj["name"]: obj["level"] for obj in objects}
        if len(levels) != len(objects) or any(obj["level"] == 2 and levels.get(obj["base-name"]) != 1
                                             for obj in objects):
            raise ValueError("导出结果存在重复名称或缺少父区域")
    if not files:
        raise ValueError("区域配置与边界没有匹配结果")
    return files


def write_package(folder, commit, version, entries, areas, unmatched):
    files = make_profiles(areas)
    for rel, objects in files.items():
        write_json(folder / rel, objects)
    missing = [a["configId"] for a in areas if not a["shapes"]]
    file_counts = {p: len(v) for p, v in sorted(files.items())}
    metadata = {"version": version, "commit": commit, "sourceRepository": REPO,
                "sourceBlobs": {e["path"]: e["sha"] for e in entries},
                "minecraft": {"coordinateTransform": "Xmc=-Zgenshin, Ymc=Ygenshin, Zmc=-Xgenshin",
                              "unmappedConfigIds": missing, "perFileRecordCount": file_counts}}
    write_json(folder / "reference" / "areas-index.json",
               {"metadata": metadata, "areas": areas, "unmatchedGeometry": unmatched})
    counts = {profile: sum(n for p, n in file_counts.items() if p.startswith(profile + "/"))
              for profile in ("all", "tips-only")}
    guide = [
        "# 原神地名 · Minecraft Areas Hint 导入包", "",
        f"数据版本 {version}。[固定提交](https://github.com/{REPO}/tree/{commit})", "",
        "坐标按 MC (X, Y, Z) = 原神 (-Z, Y, -X) 转换，比例 1:1。各场景保留原高度。",
        "",
        f"全部区域版包含 {counts['all']} 份轮廓，仅提示版包含 {counts['tips-only']} 份轮廓。"
        f"共有 {len(missing)} 条区域配置缺少匹配边界。区域 ID、中文名、原始顶点和对应的 MC 记录可在"
        " [转换索引](reference/areas-index.json) 中查找。", "",
        "安装 Areas Hint 后进入目标世界一次。选择一套文件，从对应场景目录取出 overworld.json，"
        "放入 MC 实例的 areas-hint/ 目录下由模组生成的世界子目录。单人世界目录通常为 localhost_<世界名>。"
        "已有自建区域时先备份，按内部名称合并或更新，再运行 /areahint reload。",
        "",
        "提瓦特大世界为场景 3。重名轮廓使用唯一内部名称，标题沿用中文地名。"
        "在仅提示版中，缺少已启用父区域的子区域作为一级区域导入。",
        "", "| 场景 ID | 全部区域 | 仅 showTips=true |", "|---:|---:|---:|",
    ]
    for scene in sorted({a["sceneId"] for a in areas if a["shapes"]}):
        cells = []
        for profile in counts:
            p = f"{profile}/scene-{scene}/overworld.json"
            cells.append(f"[{file_counts[p]}]({p})" if p in file_counts else "—")
        guide.append(f"| {scene} | {cells[0]} | {cells[1]} |")
    guide += ["", "[Areas Hint 使用说明](https://github.com/IMSng1019/area-hints)", ""]
    (folder / "README.md").write_bytes("\n".join(guide).encode("utf-8"))
    print(f"全部区域 {counts['all']} 份，提示区域 {counts['tips-only']} 份，"
          f"缺少边界 {len(missing)} 条，缺少配置的轮廓 {len(unmatched)} 份。")


def publish(folder, output, name):
    paths = sorted(p.relative_to(folder) for p in folder.rglob("*") if p.is_file())
    archive = folder / "area-hints.zip"
    with zipfile.ZipFile(archive, "w") as z:
        for rel in paths:
            info = zipfile.ZipInfo(rel.as_posix())  # 固定时间戳，使相同内容可以重复生成。
            z.writestr(info, (folder / rel).read_bytes(), compress_type=zipfile.ZIP_DEFLATED)
    with zipfile.ZipFile(archive) as z:
        if z.testzip() is not None:
            raise ValueError("ZIP 完整性校验失败")
    paths.append(Path(archive.name))
    # 整个目录一次发布。遇到用户改过的输出或并发运行时保留双方的数据。
    index = 1
    while True:
        target = output / (name if index == 1 else f"{name}-{index}")
        try:
            if not target.exists():
                folder.rename(target)
                return target
        except OSError:
            if not target.exists():
                raise
        if all((target / p).is_file() and (target / p).read_bytes() == (folder / p).read_bytes()
               for p in paths):
            return target
        index += 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ref", default="main", help="上游分支、标签或提交 SHA（默认 main）")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT, help="输出根目录（默认仓库的 exports）")
    args = parser.parse_args()
    output = args.output.resolve()
    print(f"正在获取 {REPO} 的 {args.ref} 版本……", flush=True)
    commit, version, entries = snapshot(args.ref)
    print(f"{version}  ({commit[:12]})", flush=True)
    cache = output / ".area-hints-cache"
    cache.mkdir(parents=True, exist_ok=True)
    blobs = {entry["sha"]: entry for entry in entries}
    with ThreadPoolExecutor(max_workers=4) as pool:
        paths = pool.map(lambda entry: cached_file(cache, commit, entry), blobs.values())
        downloaded = dict(zip(blobs, paths))
    files = {e["path"]: downloaded[e["sha"]] for e in entries}
    areas, unmatched = collect_areas(files)
    version_number = re.search(r"RELWin(\d+\.\d+\.\d+)", version)
    stamp = version_number[1] + "-" if version_number else ""
    name = f"area-hints-genshin-{stamp}{commit[:12]}"
    with tempfile.TemporaryDirectory(prefix=".area-hints-build-", dir=output) as temp:
        folder = Path(temp) / "package"
        write_package(folder, commit, version, entries, areas, unmatched)
        target = publish(folder, output, name)
    print(f"已生成 {target / 'area-hints.zip'}")
    world = target / "all/scene-3/overworld.json"
    if world.is_file():
        print(f"提瓦特主世界 {world}")


if __name__ == "__main__":
    try:
        main()
    except (OSError, HTTPException, ValueError, KeyError, TypeError) as exc:
        print(f"导出失败：{exc}", file=sys.stderr)
        sys.exit(1)

