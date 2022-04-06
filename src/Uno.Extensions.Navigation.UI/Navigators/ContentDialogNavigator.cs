﻿using Uno.Extensions.Navigation.Regions;
using Uno.Extensions.Navigation.UI;

namespace Uno.Extensions.Navigation.Navigators;

public class ContentDialogNavigator : DialogNavigator
{
	public ContentDialogNavigator(
		ILogger<ContentDialogNavigator> logger,
		IResolver resolver,
		IRegion region
#if WINUI
		, IWindowProvider windowProvider
#endif
		)
		: base(logger, resolver, region
#if WINUI
		, windowProvider
#endif
		)
	{
	}

	protected override bool CanNavigateToRoute(Route route) =>
			base.CanNavigateToRoute(route) &&
			(Resolver.Routes.Find(route)?.View?.RenderView?.IsSubclassOf(typeof(ContentDialog)) ?? false);

	protected override async Task<IAsyncInfo?> DisplayDialog(NavigationRequest request, Type? viewType, object? viewModel)
	{
		var route = request.Route;
		var navigation = Region.Navigator();
		var services = this.Get<IServiceProvider>();
		var mapping = Resolver.Routes.Find(route);
		if (
			navigation is null ||
			services is null ||
			mapping?.View?.RenderView is null)
		{
			return null;
		}

		var dialog = Activator.CreateInstance(mapping.View.RenderView) as ContentDialog;
		if (dialog is null)
		{
			return null;
		}

#if WINUI
		dialog.XamlRoot = WindowProvider.Current.Content.XamlRoot;
#endif

		dialog.SetInstance(Region);

		dialog.InjectServicesAndSetDataContext(services, navigation, viewModel);

		var showTask = dialog.ShowAsync();
		showTask.AsTask()
			.ContinueWith(result =>
				{
					if (result.Status != TaskStatus.Canceled)
					{
						navigation.NavigateBackWithResultAsync(request.Sender, data: Option.Some(result.Result));
					}
				},
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.DenyChildAttach,
				TaskScheduler.FromCurrentSynchronizationContext());

		await dialog.EnsureLoaded();

		if (dialog.Content is FrameworkElement dialogElement)
		{
			dialogElement.SetName(route.Base);
			dialogElement.ReassignRegionParent();
		}


		if (request.Cancellation.HasValue &&
			request.Cancellation.Value.CanBeCanceled)
		{
			request.Cancellation.Value.Register(() =>
			{
				showTask.Cancel();
			});
		}

		return showTask;
	}
}
