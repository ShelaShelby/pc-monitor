using System.Windows.Controls;
using PcMonitor.Core;

namespace PcMonitor.Ui.Tests;

public class LayoutBoxTests
{
    [Fact]
    public void SelectedItemHasTextForEveryMode() => Sta.Run(() =>
    {
        foreach (LayoutMode mode in Enum.GetValues<LayoutMode>())
        {
            // Same bindings as MainWindow.xaml's LayoutBox.
            var box = new ComboBox();
            box.BeginInit();
            box.ItemsSource = LayoutFit.Options;
            box.SelectedValuePath = "Mode";
            box.EndInit();
            box.SelectedValue = mode;
            LayoutOption item = Assert.IsType<LayoutOption>(box.SelectedItem);
            Assert.Equal(mode, item.Mode);
            Assert.False(string.IsNullOrWhiteSpace(item.Name), $"{mode} has no text");
            // The closed box shows SelectionBoxItem as text; it must read as the mode name.
            Assert.Equal(item.Name, item.ToString());
        }
    });
}
