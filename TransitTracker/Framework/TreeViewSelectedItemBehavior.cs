using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Xaml.Behaviors;

namespace TransitTracker.Framework;

public class TreeViewSelectedItemBehavior : Behavior<TreeView>
{
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register("SelectedItem", typeof(object), typeof(TreeViewSelectedItemBehavior),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

    public object SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.SelectedItemChanged += OnTreeViewSelectedItemChanged;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        AssociatedObject.SelectedItemChanged -= OnTreeViewSelectedItemChanged;
    }

    private void OnTreeViewSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TransitVehicle)
            return;

        if (SelectedItem != e.NewValue)
            SelectedItem = e.NewValue;
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeViewSelectedItemBehavior behavior && behavior.AssociatedObject != null)
        {
            behavior.AssociatedObject.SelectedItemChanged -= behavior.OnTreeViewSelectedItemChanged;
            behavior.AssociatedObject.SelectedItemChanged += behavior.OnTreeViewSelectedItemChanged;

            if (e.NewValue != null)
            {
                behavior.SetSelectedItem(behavior.AssociatedObject, e.NewValue);
            }
        }
    }

    private void SetSelectedItem(TreeView treeView, object selectedItem)
    {
        var item = FindTreeViewItem(treeView, selectedItem);
        if (item != null)
        {
            item.IsSelected = true;
            item.BringIntoView();
        }
    }

    private TreeViewItem? FindTreeViewItem(ItemsControl container, object item)
    {
        if (container == null)
        {
            return null;
        }

        if (container.DataContext == item)
        {
            return container as TreeViewItem;
        }

        container.ApplyTemplate();
        if (container.Template.FindName("ItemsHost", container) is ItemsPresenter itemsPresenter)
        {
            itemsPresenter.ApplyTemplate();
        }
        else
        {
            if (VisualTreeHelper.GetChild(container, 0) is Panel itemsHostPanel)
            {
                itemsHostPanel.ApplyTemplate();
            }
        }

        for (int i = 0; i < container.Items.Count; i++)
        {
            if (container.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem subContainer)
            {
                continue;
            }

            var resultContainer = FindTreeViewItem(subContainer, item);
            if (resultContainer != null)
            {
                return resultContainer;
            }
        }

        return null;
    }
}