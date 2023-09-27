using System.Collections.ObjectModel;
using HotReloadDemo.Models;

namespace HotReloadDemo.ViewModels;

public class ToDoListViewModel : ViewModelBase
{
    public ObservableCollection<ToDoItem> ToDoItems { get; }

    public ToDoListViewModel(IEnumerable<ToDoItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        ToDoItems = new(items);
    }
}
