using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.RealTime;
using Esri.ArcGISRuntime.Toolkit.UI;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;

namespace TransitTracker;

public class MapViewController : GeoViewController
{
    public Task PanToAsync(MapPoint? center)
    {
        if (ConnectedView is null || center is null)
            return Task.CompletedTask;
        return ConnectedView.SetViewpointAsync(new Viewpoint(center));
    }

    public void ShowCalloutForGeoElement(DynamicEntity train, CalloutDefinition calloutDef)
    {
        if (ConnectedView is not MapView mapView || train.Geometry is not MapPoint point)
            return;

        calloutDef.ButtonImage = new RuntimeImage(new Uri("Content/info.png", UriKind.Relative));
        calloutDef.LeaderOffsetY = 15;
        var position = mapView.LocationToScreen(point);
        mapView.ShowCalloutForGeoElement(train, position, calloutDef);
    }
}
