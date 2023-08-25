﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static class VisualElementExtensions
{
    public static void RegisterCallbackButtonTriggered(this Button button, EventCallback<EventBase> callback)
    {
        button.RegisterCallback<ClickEvent>(callback);
        button.RegisterCallback<NavigationSubmitEvent>(callback);
    }

    public static void UnregisterCallbackButtonTriggered(this Button button, EventCallback<EventBase> callback)
    {
        button.UnregisterCallback<ClickEvent>(callback);
        button.UnregisterCallback<NavigationSubmitEvent>(callback);
    }

    public static void AddToClassListIfNew(this VisualElement visualElement, params string[] newClasses)
    {
        HashSet<string> currentClasses = new();
        visualElement.GetClasses().ForEach(currentClass => currentClasses.Add(currentClass));
        newClasses.ForEach(newClass =>
        {
            if (!currentClasses.Contains(newClass))
            {
                visualElement.AddToClassList(newClass);
            }
        });
    }

    public static void SetInClassList(this VisualElement visualElement, string newClass, bool shouldBePresent)
    {
        if (shouldBePresent)
        {
            visualElement.AddToClassList(newClass);
        }
        else
        {
            visualElement.RemoveFromClassList(newClass);
        }
    }

    public static bool IsVisibleByDisplay(this VisualElement visualElement)
    {
        return visualElement.resolvedStyle.display != DisplayStyle.None;
    }

    public static void SetVisibleByDisplay(this VisualElement visualElement, bool isVisible)
    {
        if (isVisible)
        {
            visualElement.ShowByDisplay();
        }
        else
        {
            visualElement.HideByDisplay();
        }
    }

    public static void ToggleVisibleByDisplay(this VisualElement visualElement)
    {
        visualElement.SetVisibleByDisplay(!visualElement.IsVisibleByDisplay());
    }

    public static void ShowByDisplay(this VisualElement visualElement)
    {
        visualElement.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);
    }

    public static void HideByDisplay(this VisualElement visualElement)
    {
        visualElement.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
    }

    public static bool IsVisibleByVisibility(this VisualElement visualElement)
    {
        return visualElement.style.visibility != Visibility.Hidden;
    }

    public static void SetVisibleByVisibility(this VisualElement visualElement, bool isVisible)
    {
        if (isVisible)
        {
            visualElement.ShowByVisibility();
        }
        else
        {
            visualElement.HideByVisibility();
        }
    }

    public static void ToggleVisibleByVisibility(this VisualElement visualElement)
    {
        visualElement.SetVisibleByVisibility(!visualElement.IsVisibleByVisibility());
    }

    public static void ShowByVisibility(this VisualElement visualElement)
    {
        visualElement.style.visibility = new StyleEnum<Visibility>(Visibility.Visible);
    }

    public static void HideByVisibility(this VisualElement visualElement)
    {
        visualElement.style.visibility = new StyleEnum<Visibility>(Visibility.Hidden);
    }

    /**
     * Executes the given callback at most once when the event occurs.
     */
    public static void RegisterCallbackOneShot<TEventType>(
        this VisualElement visualElement,
        EventCallback<TEventType> callback,
        TrickleDown useTrickleDown = TrickleDown.NoTrickleDown)
        where TEventType : EventBase<TEventType>, new()
    {
        bool wasExecuted = false;
        void RunCallbackIfNotDoneYet(TEventType evt)
        {
            if (!wasExecuted)
            {
                wasExecuted = true;
                callback(evt);
                visualElement.UnregisterCallback<TEventType>(RunCallbackIfNotDoneYet, useTrickleDown);
            }
        }

        visualElement.RegisterCallback<TEventType>(RunCallbackIfNotDoneYet, useTrickleDown);
    }

    public static void SetBackgroundImageAlpha(this VisualElement visualElement, float newAlpha)
    {
        Color lastColor = visualElement.resolvedStyle.unityBackgroundImageTintColor;
        visualElement.style.unityBackgroundImageTintColor = new Color(lastColor.r, lastColor.g, lastColor.b, newAlpha);
    }

    // Make the color of an image darker with a factor < 1, or brighter with a factor > 1.
    public static void MultiplyBackgroundImageColor(this VisualElement visualElement, float factor, bool includeAlpha = false)
    {
        Color lastColor = visualElement.resolvedStyle.unityBackgroundImageTintColor;
        float newR = NumberUtils.Limit(lastColor.r * factor, 0, 1);
        float newG = NumberUtils.Limit(lastColor.g * factor, 0, 1);
        float newB = NumberUtils.Limit(lastColor.b * factor, 0, 1);
        float newAlpha = includeAlpha ? NumberUtils.Limit(lastColor.a * factor, 0, 1) : lastColor.a;
        visualElement.style.unityBackgroundImageTintColor = new Color(newR, newG, newB, newAlpha);
    }

    public static void ScrollToSelf(this VisualElement visualElement)
    {
        if (visualElement == null)
        {
            return;
        }

        List<ScrollView> ancestorScrollViews = visualElement
            .GetAncestors()
            .OfType<ScrollView>()
            .ToList();
        ancestorScrollViews.ForEach(scrollView => scrollView.ScrollTo(visualElement));
    }

    public static List<VisualElement> GetAncestors(this VisualElement visualElement)
    {
        if (visualElement == null)
        {
            return new List<VisualElement>();
        }

        List<VisualElement> ancestors = new();
        VisualElement parent = visualElement.parent;
        while (parent != null)
        {
            ancestors.Add(parent);
            parent = parent.parent;
        }
        return ancestors;
    }

    public static void SetBorderColor(this VisualElement visualElement, Color color)
    {
        visualElement.style.borderLeftColor = color;
        visualElement.style.borderRightColor = color;
        visualElement.style.borderTopColor = color;
        visualElement.style.borderBottomColor = color;
    }
    
    public static void SetBorderWidth(this VisualElement visualElement, float value)
    {
        visualElement.style.borderLeftWidth = value;
        visualElement.style.borderRightWidth = value;
        visualElement.style.borderTopWidth = value;
        visualElement.style.borderBottomWidth = value;
    }

    public static void SetSelectionAndScrollTo(this ListView listView, int index)
    {
        listView.SetSelection(index);
        listView.ScrollToItem(index);
    }
    
    public static VisualElement GetSelectedVisualElement(this ListView listView)
    {
        return listView.Q<VisualElement>(className: "unity-collection-view__item--selected");
    }

    public static VisualElement GetParent(this VisualElement visualElement, Func<VisualElement, bool> condition=null)
    {
        VisualElement parent = visualElement.parent;
        while (parent != null)
        {
            if (condition == null
                || condition(parent))
            {
                return parent;
            }
            parent = parent.parent;
        }

        return null;
    }
    
    public static List<VisualElement> GetParents(VisualElement visualElement)
    {
        List<VisualElement> parents = new();
        VisualElement parent = visualElement.parent;
        while (parent != null)
        {
            parents.Add(parent);
            parent = parent.parent;
        }

        return parents;
    }
}
