using System.Threading.Tasks;

public interface IResourceProvider
{
    Task<object> ProcessResource(byte[] data, string url);
}