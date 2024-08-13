using System.Threading.Tasks;

public class BinaryProvider : IResourceProvider
{
    public Task<object> ProcessResource(byte[] data, string url)
    {
        return Task.FromResult((object)data);
    }
}