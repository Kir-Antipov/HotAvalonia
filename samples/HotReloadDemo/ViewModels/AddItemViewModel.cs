using System.Reactive;
using HotReloadDemo.Models;
using ReactiveUI;

namespace HotReloadDemo.ViewModels;

public class AddItemViewModel : ViewModelBase
{
    private string _description = string.Empty;

    public AddItemViewModel()
    {
        IObservable<bool> isValidObservable = this.WhenAnyValue(
            x => x.Description,
            x => !string.IsNullOrWhiteSpace(x));

        OkCommand = ReactiveCommand.Create(() => new ToDoItem { Description = Description }, isValidObservable);
        CancelCommand = ReactiveCommand.Create(() => { });
    }

    public string Description
    {
        get => _description;
        set => this.RaiseAndSetIfChanged(ref _description, value);
    }

    public ReactiveCommand<Unit, ToDoItem> OkCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
}
