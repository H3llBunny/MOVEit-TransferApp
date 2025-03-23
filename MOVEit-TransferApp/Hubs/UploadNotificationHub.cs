using Microsoft.AspNetCore.SignalR;

namespace MOVEit_TransferApp.Hubs
{
    public class UploadNotificationHub : Hub
    {
        private static string _connetionId;

        public static string GetConnectionId()
        {
            return _connetionId;
        }

        public override async Task OnConnectedAsync()
        {
            _connetionId = Context.ConnectionId;
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            _connetionId = null;
            await base.OnDisconnectedAsync(exception);
        }

        public async Task FolderNotification(string fileName, int size)
        {
            if (_connetionId != null)
            {
                await Clients.Client(Context.ConnectionId).SendAsync("ReceiveNotification", fileName, size);
            }
        }
    }
}
