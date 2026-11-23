namespace CamPos.Core.Camera;

public interface ICameraSource : IDisposable
{
    string Description { get; }
    CameraReadResult Read();
}
