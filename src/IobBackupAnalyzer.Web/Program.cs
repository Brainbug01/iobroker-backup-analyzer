using IobBackupAnalyzer.Core;
using IobBackupAnalyzer.Web;
using IobBackupAnalyzer.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Deutsche Zahlen- und Datumsformate, gleiche Quelle wie in den Desktop-Fassungen.
// Ohne diesen Aufruf richtet sich die Anzeige nach der Spracheinstellung des Browsers,
// und dieselbe Zahl stünde je nach Besucher als „1.234" oder „1,234" da.
AppCulture.Apply();

// Die Browser-Fassung liegt hinter den Desktop-Fassungen zurück. Ohne diese Zeile stünde
// in jedem hier erzeugten Aufräum-Skript die neueste Nummer aus dem Änderungsverlauf statt
// der Fassung, die es wirklich geschrieben hat.
AppIdentity.SetRunningVersion(AppInfo.Version);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<AppState>();
builder.Services.AddSingleton<UiState>();
builder.Services.AddSingleton<DialogService>();
builder.Services.AddScoped<BrowserIo>();
builder.Services.AddScoped<WebSettings>();

await builder.Build().RunAsync();
