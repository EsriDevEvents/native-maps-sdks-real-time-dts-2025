using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace TransitTracker.Framework;

public class PopupBorder : Border
{
    static PopupBorder()
    {
        // Register the default style for this custom control
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PopupBorder), new FrameworkPropertyMetadata(typeof(PopupBorder)));
    }

    public PopupBorder()
    {
        DataContextChanged += CustomBorder_DataContextChanged;
    }

    private void CustomBorder_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Trigger the animation or any other action when DataContext changes
        StartColorAnimation();
    }

    private void StartColorAnimation()
    {
        // Ensure the Border has a DropShadowEffect
        if (Effect is DropShadowEffect dropShadowEffect)
        {
            // Create a new DropShadowEffect with the same properties but not frozen
            var newDropShadowEffect = new DropShadowEffect
            {
                Color = dropShadowEffect.Color,
                ShadowDepth = dropShadowEffect.ShadowDepth,
                BlurRadius = dropShadowEffect.BlurRadius,
                Direction = dropShadowEffect.Direction,
                Opacity = dropShadowEffect.Opacity
            };

            // Apply the new DropShadowEffect to the Border
            Effect = newDropShadowEffect;

            // Create a ColorAnimationUsingKeyFrames to animate the new DropShadowEffect's color
            var colorAnimation = new ColorAnimationUsingKeyFrames();
            colorAnimation.KeyFrames.Add(new LinearColorKeyFrame(Colors.Red, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1))));
            colorAnimation.KeyFrames.Add(new LinearColorKeyFrame(Colors.Gray, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4))));

            // Apply the animation to the new DropShadowEffect's Color property
            newDropShadowEffect.BeginAnimation(DropShadowEffect.ColorProperty, colorAnimation);
        }
    }
}
