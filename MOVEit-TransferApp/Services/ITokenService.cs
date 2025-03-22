namespace MOVEit_TransferApp.Services
{
    public interface ITokenService
    {
        Task<bool> RequestTokenAsync(string username, string password);
    }
}
