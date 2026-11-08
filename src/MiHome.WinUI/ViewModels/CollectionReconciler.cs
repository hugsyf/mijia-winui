using System.Collections.ObjectModel;
namespace MiHome.WinUI.ViewModels;

internal static class CollectionReconciler
{
    // Preserve existing objects and use Move notifications instead of Reset.
    public static void Apply<T>(ObservableCollection<T> collection, IReadOnlyList<T> desired, Func<T,string> key)
    {
        var retained = desired.Select(key).ToHashSet();
        for (var i = collection.Count - 1; i >= 0; i--)
            if (!retained.Contains(key(collection[i]))) collection.RemoveAt(i);
        for (var i = 0; i < desired.Count; i++)
        {
            var found = -1;
            for (var j = i; j < collection.Count; j++)
                if (key(collection[j]) == key(desired[i])) { found = j; break; }
            if (found < 0) collection.Insert(i, desired[i]);
            else
            {
                if (found != i) collection.Move(found, i);
                if (!EqualityComparer<T>.Default.Equals(collection[i], desired[i])) collection[i] = desired[i];
            }
        }
    }
}
