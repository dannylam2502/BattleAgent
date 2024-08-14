using System.Net.Http;
using Cysharp.Threading.Tasks;

public class HttpClientDownloader : IDownloader
{
    private readonly HttpClient httpClient;

    public HttpClientDownloader()
    {
        httpClient = new HttpClient();
    }

    public async UniTask<HttpResponseMessage> DownloadData(string url)
    {
        return await httpClient.GetAsync(url);
    }

    public void Dispose()
    {
        httpClient?.Dispose();
    }
}