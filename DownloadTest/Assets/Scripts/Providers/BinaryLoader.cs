using System.Threading.Tasks;

public class BinaryProvider
{
    public Task<object> ProcessResource(byte[] data, string url)
    {
        return Task.FromResult((object)data);
    }
}