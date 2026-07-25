using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using Datamatrix_Notepad;
using Datamatrix_Notepad.Services.Export;
using Datamatrix_Notepad.Services.Serial;
using Datamatrix_Notepad.Services.State;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddFluentUIComponents();
builder.Services.AddScoped<SerialPortService>();
builder.Services.AddScoped<IAppStateStore, BrowserAppStateStore>();
builder.Services.AddScoped<AppStateService>();
builder.Services.AddScoped<IExportFileNameService, ExportFileNameService>();
builder.Services.AddScoped<IBrowserFileDownloader, BrowserFileDownloader>();
builder.Services.AddScoped<NoteExportService>();

await builder.Build().RunAsync();
