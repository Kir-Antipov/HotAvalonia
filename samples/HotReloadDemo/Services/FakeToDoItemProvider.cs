using HotReloadDemo.Models;

namespace HotReloadDemo.Services;

public sealed class FakeToDoItemProvider : IToDoItemProvider
{
    public IEnumerable<ToDoItem> GetItems() => new[]
    {
        new ToDoItem { Description = "Walk the dog" },
        new ToDoItem { Description = "Buy some milk" },
        new ToDoItem { Description = "Learn Avalonia", IsChecked = true },
    };
}
