using System.Threading.Tasks;

public interface IResourceLoader
{
    Task<object> LoadResourceAsync(byte[] data);

    UnityEngine.Object LoadResource(byte[] data);
}