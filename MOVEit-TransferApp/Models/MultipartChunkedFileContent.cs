using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MOVEit_TransferApp.Models
{
    public class MultipartChunkedFileContent : HttpContent
    {
        private readonly string _filePath;
        private readonly string _fileName;
        private const int ChunkSize = 10485760; //10 MB
        private readonly string _boundary = Guid.NewGuid().ToString("N");

        public MultipartChunkedFileContent(string filePath, string fileName)
        {
            _filePath = filePath;
            _fileName = fileName;
            Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");
            Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", _boundary));
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            using var fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read);
            byte[] buffer = new byte[ChunkSize];
            int bytesRead;
            string header = $"--{_boundary}\r\n" +
                            $"Content-Disposition: form-data; name=\"file\"; filename=\"{_fileName}\"\r\n" +
                            $"Content-Type: application/octet-stream\r\n\r\n";

            byte[] headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);

            try
            {
                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    try
                    {
                        await stream.WriteAsync(buffer, 0, bytesRead);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error writing chunk: {ex.Message}");
                        throw;
                    }
                }

                byte[] trailer = Encoding.UTF8.GetBytes($"\r\n--{_boundary}--\r\n");
                await stream.WriteAsync(trailer, 0, trailer.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during file upload: {ex.Message}");
                throw;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
