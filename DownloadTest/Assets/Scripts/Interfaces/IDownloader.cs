using System.Threading.Tasks;

public interface IDownloader
{
    Task<byte[]> DownloadData(string url);
}