using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisionMaven.Core.Abstractions;
using VisionMaven.Modules.Algorithm.ViewModels;

namespace VisionMaven.Modules.Algorithm.Views;

/// <summary>算法页。</summary>
public partial class AlgorithmView : UserControl
{
    public AlgorithmView()
    {
        InitializeComponent();
    }

    private void OnOperatorSelected(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element
            || DataContext is not AlgorithmViewModel viewModel
            || element.DataContext is not OperatorDescriptor descriptor)
        {
            return;
        }

        viewModel.SelectedOperator = descriptor;
    }
}
