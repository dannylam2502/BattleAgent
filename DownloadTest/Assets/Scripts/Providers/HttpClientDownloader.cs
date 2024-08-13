using System.Net.Http;
using Cysharp.Threading.Tasks;

public class HttpClientDownloader : IDownloader
{
    private readonly HttpClient httpClient;

    public HttpClientDownloader()
    {
        httpClient = new HttpClient();
    }

    public async UniTask<byte[]> DownloadData(string url)
    {
        var response = await httpClient.GetAsync(url);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsByteArrayAsync();
        }
        return null;
    }

    public void Dispose()
    {
        httpClient?.Dispose();
    }
}