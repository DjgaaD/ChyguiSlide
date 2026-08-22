using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ChyguiSlide.Views;

public sealed partial class LogsPage : Page
{
    public LogsViewModel ViewModel { get; }

    public LogsPage()
    {
        ViewModel = App.AppHost.Services.GetRequiredService<LogsViewModel>();
        InitializeComponent();
    }
}
