using Avalonia.Controls;
using Avalonia.Controls.Templates;
using HotReloadDemo.ViewModels;

namespace HotReloadDemo;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        string? name = data?.GetType().FullName?.Replace("ViewModel", "View");
        Type? type = name is null ? null : Type.GetType(name);

        if (type is not null)
            return (Control?)Activator.CreateInstance(type);

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
