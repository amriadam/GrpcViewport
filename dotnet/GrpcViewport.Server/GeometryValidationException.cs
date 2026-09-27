namespace GrpcViewport.Server;

public sealed class GeometryValidationException(string message) : ArgumentException(message);
