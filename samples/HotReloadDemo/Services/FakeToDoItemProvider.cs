using HotReloadDemo.Models;

namespace HotReloadDemo.Services;

public sealed class FakeToDoItemProvider : IToDoItemProvider
{
    public IEnumerable<ToDoItem> GetItems() => new[]
    {
        new ToDoItem { Description = "walk the dog" },
        new ToDoItem { Description = "buy some milk" },
        new ToDoItem { Description = "learn Avalonia", IsChecked = true },
    };
}
