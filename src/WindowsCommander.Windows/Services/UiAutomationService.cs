using System.Collections.Concurrent;
using System.Windows.Automation;
using WindowsCommander.Core.Models;
using WindowsCommander.Core.Services;

namespace WindowsCommander.Windows.Services;

public sealed class UiAutomationService : IUiAutomationService
{
    private readonly ConcurrentDictionary<string, AutomationElement> elements = new(StringComparer.OrdinalIgnoreCase);

    public UiTreeResult ReadUiTree(long windowHandle, int maxDepth = 5, IReadOnlyList<string>? controlTypes = null, bool interactableOnly = false)
    {
        var depth = Math.Clamp(maxDepth, 1, 20);
        var root = GetRootElement(windowHandle);
        var truncated = false;

        // Flatten is lazy; the filters compose on top of it. ToArray() still
        // forces the full descent, so 'truncated' reflects the whole tree
        // regardless of how many elements the filters keep.
        IEnumerable<UiElementInfo> flattened = Flatten(root, depth, () => truncated = true);

        if (controlTypes is { Count: > 0 })
        {
            var wanted = new HashSet<string>(controlTypes, StringComparer.OrdinalIgnoreCase);
            flattened = flattened.Where(element => wanted.Contains(element.ControlType));
        }

        if (interactableOnly)
        {
            flattened = flattened.Where(IsInteractable);
        }

        var elements = flattened.ToArray();
        return new UiTreeResult(elements, truncated, depth, elements.Length);
    }

    // True when the element exposes a pattern an agent can act on. Used by
    // read_ui_tree's interactable_only filter to drop purely structural noise
    // (panes, static text, group containers) and shrink the payload.
    private bool IsInteractable(UiElementInfo element)
    {
        if (!elements.TryGetValue(element.ElementRef, out var automationElement))
        {
            return false;
        }

        return automationElement.TryGetCurrentPattern(InvokePattern.Pattern, out _)
            || automationElement.TryGetCurrentPattern(TogglePattern.Pattern, out _)
            || automationElement.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _)
            || automationElement.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _)
            || automationElement.TryGetCurrentPattern(ValuePattern.Pattern, out _);
    }

    public IReadOnlyList<UiElementInfo> FindUiElement(long windowHandle, string? nameContains, string? automationId, string? controlType, string? className, bool enabledOnly, int maxDepth = 5)
    {
        return ReadUiTree(windowHandle, maxDepth).Elements
            .Where(element => Matches(element, nameContains, automationId, controlType, className, enabledOnly))
            .ToArray();
    }

    public UiActionResult InvokeUiElement(string elementRef, string action)
    {
        var element = GetElement(elementRef);
        switch (action.ToLowerInvariant())
        {
            case "invoke" when element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePattern):
                ((InvokePattern)invokePattern).Invoke();
                break;
            case "select" when element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionPattern):
                ((SelectionItemPattern)selectionPattern).Select();
                break;
            case "expand" when element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandPattern):
                ((ExpandCollapsePattern)expandPattern).Expand();
                break;
            case "collapse" when element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var collapsePattern):
                ((ExpandCollapsePattern)collapsePattern).Collapse();
                break;
            case "toggle" when element.TryGetCurrentPattern(TogglePattern.Pattern, out var togglePattern):
                ((TogglePattern)togglePattern).Toggle();
                break;
            case "focus":
                element.SetFocus();
                break;
            default:
                throw new InvalidOperationException($"UI element does not support action: {action}");
        }

        return new UiActionResult(elementRef, action, Completed: true);
    }

    public UiActionResult SetUiValue(string elementRef, string value)
    {
        var element = GetElement(elementRef);
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
        {
            throw new InvalidOperationException("UI element does not support ValuePattern.");
        }

        ((ValuePattern)pattern).SetValue(value);
        return new UiActionResult(elementRef, "set_value", Completed: true);
    }

    public UiElementDetails GetUiElementDetails(string elementRef)
    {
        var element = GetElement(elementRef);
        var info = ToInfo(element);
        var parent = TreeWalker.ControlViewWalker.GetParent(element);
        var children = EnumerateChildren(element).Select(ToInfo).ToArray();
        var actions = GetSupportedActions(element);

        return new UiElementDetails(info, actions, SafeName(parent), children);
    }

    private AutomationElement GetRootElement(long windowHandle)
    {
        return AutomationElement.FromHandle(new IntPtr(windowHandle)) ?? throw new ArgumentException($"Window handle was not found: {windowHandle}");
    }

    private AutomationElement GetElement(string elementRef)
    {
        return elements.TryGetValue(elementRef, out var element)
            ? element
            : throw new ArgumentException($"Unknown UI element reference: {elementRef}");
    }

    private IEnumerable<UiElementInfo> Flatten(AutomationElement root, int maxDepth, Action onTruncated)
    {
        foreach (var element in FlattenElements(root, 0, maxDepth, onTruncated))
        {
            yield return ToInfo(element);
        }
    }

    private static IEnumerable<AutomationElement> FlattenElements(AutomationElement root, int depth, int maxDepth, Action onTruncated)
    {
        yield return root;
        if (depth >= maxDepth)
        {
            // The deepest visited level still has unvisited children: the
            // returned tree is incomplete, so signal truncation to the caller.
            if (TreeWalker.ControlViewWalker.GetFirstChild(root) is not null)
            {
                onTruncated();
            }

            yield break;
        }

        foreach (var child in EnumerateChildren(root))
        {
            foreach (var nested in FlattenElements(child, depth + 1, maxDepth, onTruncated))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<AutomationElement> EnumerateChildren(AutomationElement element)
    {
        var child = TreeWalker.ControlViewWalker.GetFirstChild(element);
        while (child is not null)
        {
            yield return child;
            child = TreeWalker.ControlViewWalker.GetNextSibling(child);
        }
    }

    private UiElementInfo ToInfo(AutomationElement element)
    {
        var runtimeId = string.Join(".", element.GetRuntimeId());
        var elementRef = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(runtimeId));
        elements[elementRef] = element;
        var rectangle = element.Current.BoundingRectangle;

        return new UiElementInfo(
            elementRef,
            SafeName(element),
            element.Current.AutomationId ?? string.Empty,
            element.Current.ControlType.ProgrammaticName.Replace("ControlType.", string.Empty, StringComparison.OrdinalIgnoreCase),
            element.Current.ClassName ?? string.Empty,
            new RectBounds((int)rectangle.X, (int)rectangle.Y, (int)rectangle.Width, (int)rectangle.Height),
            element.Current.IsEnabled,
            element.Current.IsOffscreen,
            TryGetValue(element));
    }

    private static bool Matches(UiElementInfo element, string? nameContains, string? automationId, string? controlType, string? className, bool enabledOnly)
    {
        return (string.IsNullOrWhiteSpace(nameContains) || element.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(automationId) || string.Equals(element.AutomationId, automationId, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(controlType) || string.Equals(element.ControlType, controlType, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(className) || string.Equals(element.ClassName, className, StringComparison.OrdinalIgnoreCase))
            && (!enabledOnly || element.IsEnabled);
    }

    private static string? TryGetValue(AutomationElement element)
    {
        return element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
            ? ((ValuePattern)pattern).Current.Value
            : null;
    }

    private static IReadOnlyList<string> GetSupportedActions(AutomationElement element)
    {
        var actions = new List<string> { "focus" };
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out _))
        {
            actions.Add("invoke");
        }

        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _))
        {
            actions.Add("select");
        }

        if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _))
        {
            actions.Add("expand");
            actions.Add("collapse");
        }

        if (element.TryGetCurrentPattern(TogglePattern.Pattern, out _))
        {
            actions.Add("toggle");
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out _))
        {
            actions.Add("set_value");
        }

        return actions;
    }

    private static string SafeName(AutomationElement? element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        try
        {
            return element.Current.Name ?? string.Empty;
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }
}
