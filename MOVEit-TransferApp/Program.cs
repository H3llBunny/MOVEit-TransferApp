using Microsoft.AspNetCore.SignalR;
using MOVEit_TransferApp.Hubs;
using MOVEit_TransferApp.Services;
using Serilog;

namespace MOVEit_TransferApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Application is starting...");

            var builder = WebApplication.CreateBuilder(args);

            var logFilePath = Path.Combine(AppContext.BaseDirectory, "logs", "app-log.txt");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Warning()
                .WriteTo.Console()
                .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            builder.Services.AddHttpClient<ITokenService, TokenService>();
            builder.Services.AddScoped<ITokenService, TokenService>();
            builder.Services.AddHttpClient<TokenRefresher>();
            builder.Services.AddHostedService<TokenRefresher>();
            builder.Services.AddHttpClient<FolderWatchService>();
            builder.Services.AddSingleton<FolderWatchService>();

            builder.Services.AddSignalR();
            builder.Services.AddControllersWithViews();

            builder.Services.AddRazorPages().AddRazorRuntimeCompilation();

            builder.Host.UseSerilog();

            var app = builder.Build();

            Console.WriteLine("Application has been built. Ready to handle requests.");

            app.Urls.Add("https://localhost:7191");
            app.Urls.Add("http://localhost:5176");

            Console.WriteLine("Application is listening on: https://localhost:7191, http://localhost:5176");

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.MapHub<UploadNotificationHub>("/uploadNotificationHub");

            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            Console.WriteLine("Starting application...");

            app.Run();
        }
    }
}
