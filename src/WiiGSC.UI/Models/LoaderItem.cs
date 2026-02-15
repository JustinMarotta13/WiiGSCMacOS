namespace WiiGSC.UI.Models;

public class LoaderItem
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    
    public override string ToString() => Name;
}
