using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// What a test needs to act on a notice and then read the settled outcome.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a dismissal is settled.</b> bUnit's synchronous event helpers hand
/// the event to the renderer's dispatcher and return. When the dispatcher is
/// idle the event runs before they return; when it is still finishing earlier
/// work — the tail of the gesture that put the notice up — the event is queued
/// and the helper returns first, so an assertion made at once reads the screen
/// as it was. Diagnosed on halheinrich/backgammon#374 by holding the
/// dispatcher busy: a synchronous click on the run error's close button
/// returned with the box still there, and the box went once the dispatcher
/// settled. <see cref="NoticeBox.Dismiss"/> clicks synchronously and returns
/// no task, so the dismissal's own completion is not available to await.
/// </para>
/// <para>
/// The dispatcher runs its work in order, so an empty item queued after the
/// click completes only once the click has been dispatched — and with it the
/// dismissal, which is synchronous end to end (the notice raises its
/// caller-held <c>DismissedChanged</c>, and the panel's handler clears its
/// field). That is the boundary <see cref="DismissAndSettleAsync"/> awaits.
/// It is no sleep and forces no render: the render is the click's own.
/// </para>
/// </remarks>
internal static class NoticeTesting
{
    /// <summary>
    /// The guard on awaiting an operation's own completion: long enough that
    /// a loaded machine never trips it, so it fails only an operation that
    /// never completes. The completion is the evidence, not the bound.
    /// </summary>
    public static TimeSpan OperationBound { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Dismisses <paramref name="box"/> by <paramref name="gesture"/>, then
    /// waits until the dispatcher has run the dismissal.
    /// </summary>
    public static async Task DismissAndSettleAsync<TComponent>(
        this NoticeBox box, IRenderedComponent<TComponent> cut, NoticeDismissGesture gesture)
        where TComponent : IComponent
    {
        box.Dismiss(gesture);
        await cut.InvokeAsync(() => { }).WaitAsync(OperationBound);
    }
}
