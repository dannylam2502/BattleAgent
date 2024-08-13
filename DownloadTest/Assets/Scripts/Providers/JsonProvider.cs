using System.Threading.Tasks;

public class JsonProvider : IResourceProvider
{
    public Task<object> ProcessResource(byte[] data, string url)
    {
        string json = System.Text.Encoding.UTF8.GetString(data);
        return Task.FromResult((object)json);
    }
}