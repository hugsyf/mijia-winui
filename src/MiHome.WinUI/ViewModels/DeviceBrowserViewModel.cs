using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
namespace MiHome.WinUI.ViewModels;

public partial class DeviceBrowserViewModel : ObservableObject
{
    private List<DeviceIdentity> allDevices = [];
    private readonly Dictionary<string, DeviceCardState> cardStates = [];
    public IEnumerable<DeviceIdentity> AllDevices => allDevices;
    public ObservableCollection<DeviceIdentity> Devices { get; } = [];
    public ObservableCollection<DeviceTile> Tiles { get; } = [];
    public ObservableCollection<DeviceTile> Featured { get; } = [];
    private List<string> order = [];
    public bool IsEditing { get; set; }
    public bool CompactLayout { get; set; }
    public void SetOrder(IEnumerable<string> ids) => order = ids.Distinct().ToList();
    public List<string> OrderedIds => DeviceOrdering.Apply(DeviceListing.Visible(allDevices), order).Select(d => d.Id).ToList();
    public int TotalCount => allDevices.Count;
    public void SetDevices(List<DeviceIdentity> devices) { allDevices = devices; cardStates.Clear(); }
    public void UpdateCardState(DeviceCardState state) => cardStates[state.DeviceId] = state;
    public List<DeviceTile> HomeTiles => DeviceListing.Visible(allDevices).Select(d=>new DeviceTile(d,cardStates.GetValueOrDefault(d.Id))).ToList();
    public void SetCardStates(List<DeviceCardState> states)
    {
        cardStates.Clear();
        foreach (var state in states) cardStates[state.DeviceId] = state;
    }
    public void ReplaceDevice(DeviceIdentity device)
    {
        var index = allDevices.FindIndex(d => d.Id == device.Id);
        if (index >= 0) allDevices[index] = device;
    }
    public void Filter(string search, string? roomId, string? category = null)
    {
        var visible = DeviceOrdering.Apply(DeviceListing.Visible(allDevices), order).Where(d =>
            (roomId is null or "*" || d.RoomId == roomId) && (category is null || DeviceCardPresentation.Category(d,cardStates.GetValueOrDefault(d.Id))==category) &&
            (d.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || d.Model.Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
        CollectionReconciler.Apply(Devices, visible, d => d.Id);
        var tiles = visible.Select(d => new DeviceTile(d,cardStates.GetValueOrDefault(d.Id),IsEditing,CompactLayout)).ToList();
        for (var i=0;i<tiles.Count;i++)
        {
            var desired=tiles[i];
            var retained=Tiles.FirstOrDefault(t=>t.Device==desired.Device && t.Size=="expanded" && desired.Size=="expanded" && t.IsEditing==desired.IsEditing);
            if (retained is null) continue;
            retained.UpdateSnapshot(desired.State); tiles[i]=retained;
        }
        CollectionReconciler.Apply(Tiles, tiles, t => t.Device.Id);
        CollectionReconciler.Apply(Featured, tiles.Take(8).ToList(), t => t.Device.Id);
    }
    public void Reorder(IEnumerable<string> ids)
    {
        SetOrder(ids);
        var ranks = order.Select((id,index) => (id,index)).ToDictionary(x=>x.id,x=>x.index);
        CollectionReconciler.Apply(Devices, Devices.OrderBy(d=>ranks.GetValueOrDefault(d.Id,int.MaxValue)).ToList(), d=>d.Id);
        CollectionReconciler.Apply(Tiles, Tiles.OrderBy(t=>ranks.GetValueOrDefault(t.Device.Id,int.MaxValue)).ToList(), t=>t.Device.Id);
        CollectionReconciler.Apply(Featured, Tiles.Take(8).ToList(), t=>t.Device.Id);
    }
}
