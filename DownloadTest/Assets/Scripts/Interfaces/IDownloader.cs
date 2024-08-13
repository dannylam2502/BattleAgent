using Cysharp.Threading.Tasks;

public interface IDownloader
{
    UniTask<byte[]> DownloadData(string url);
}