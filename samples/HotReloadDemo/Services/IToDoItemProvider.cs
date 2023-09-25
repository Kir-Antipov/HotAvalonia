using HotReloadDemo.Models;

namespace HotReloadDemo.Services;

public interface IToDoItemProvider
{
    IEnumerable<ToDoItem> GetItems();
}
