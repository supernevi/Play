﻿using System;

public class InputActionInfo : IComparable<InputActionInfo>
{
    public const string InfoSeparator = " | ";
    
    public string ActionName { get; private set; }
    public string InfoText { get; private set; }

    public InputActionInfo(string actionName, string infoText)
    {
        ActionName = actionName;
        InfoText = infoText;
    }

    public void AddInfoText(string infoText)
    {
        if (infoText.IsNullOrEmpty())
        {
            return;
        }
        
        InfoText = InfoText.Length == 0
            ? infoText
            : InfoText + InfoSeparator + infoText;
    }

    public int CompareTo(InputActionInfo other)
    {
        return string.Compare(ActionName, other.ActionName, StringComparison.InvariantCulture);
    }
}
