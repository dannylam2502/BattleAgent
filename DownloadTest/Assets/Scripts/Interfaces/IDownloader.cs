using Cysharp.Threading.Tasks;
using System.Net.Http;

public interface IDownloader
{
    UniTask<HttpResponseMessage> DownloadData(string url);
}