// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace UnityEditor
{
    [InitializeOnLoad]
    public static class SettingsService
    {
        internal static event Action repaintAllSettingsWindow;

        public static EditorWindow OpenProjectSettings(string settingsPath = null)
        {
            return SettingsWindow.Show(SettingsScope.Project, settingsPath);
        }

        public static EditorWindow OpenUserPreferences(string settingsPath = null)
        {
            return SettingsWindow.Show(SettingsScope.User, settingsPath);
        }

        public static void NotifySettingsProviderChanged()
        {
            settingsProviderChanged?.Invoke();
        }

        public static void RepaintAllSettingsWindow()
        {
            repaintAllSettingsWindow?.Invoke();
        }

        internal static event Action settingsProviderChanged;
        internal static SettingsProvider[] FetchSettingsProviders()
        {
            return
                FetchSettingProviderFromAttribute()
                    .Concat(FetchSettingProvidersFromAttribute())
                    .Concat(FetchPreferenceItems())
                    .Where(provider => provider != null)
                    .ToArray();
        }

        internal static EditorWindow OpenUserPreferenceWindow()
        {
            return SettingsWindow.Show(SettingsScope.User);
        }

        private static IEnumerable<SettingsProvider> FetchPreferenceItems()
        {
#pragma warning disable CS0618
            var methods = AttributeHelper.GetMethodsWithAttribute<PreferenceItem>(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
#pragma warning restore CS0618
            return methods.methodsWithAttributes.Select(method =>
            {
                var callback = Delegate.CreateDelegate(typeof(Action), method.info) as Action;
                if (callback != null)
                {
#pragma warning disable CS0618
                    var attributeName = (method.attribute as PreferenceItem).name;
#pragma warning restore CS0618
                    try
                    {
                        return new SettingsProvider("Preferences/" + attributeName, SettingsScope.User) { guiHandler = searchContext => callback() };
                    }
                    catch (Exception)
                    {
                        Debug.LogError("Cannot create preference wrapper for: " + attributeName);
                    }
                }

                return null;
            });
        }

        private static IEnumerable<SettingsProvider> FetchSettingProviderFromAttribute()
        {
            var methods = AttributeHelper.GetMethodsWithAttribute<SettingsProviderAttribute>();
            return methods.methodsWithAttributes.Select(method =>
            {
                try
                {
                    var callback = Delegate.CreateDelegate(typeof(Func<SettingsProvider>), method.info) as Func<SettingsProvider>;
                    return callback?.Invoke();
                }
                catch (Exception)
                {
                    Debug.LogError("Cannot create Settings Provider for: " + method.info.Name);
                }
                return null;
            });
        }

        private static IEnumerable<SettingsProvider> FetchSettingProvidersFromAttribute()
        {
            var methods = AttributeHelper.GetMethodsWithAttribute<SettingsProviderGroupAttribute>();
            return methods.methodsWithAttributes.SelectMany(method =>
            {
                try
                {
                    var callback = Delegate.CreateDelegate(typeof(Func<SettingsProvider[]>), method.info) as Func<SettingsProvider[]>;
                    var providers = callback?.Invoke();
                    if (providers != null)
                    {
                        return providers;
                    }
                }
                catch (Exception)
                {
                    Debug.LogError("Cannot create Settings Providers for: " + method.info.Name);
                }

                return new SettingsProvider[0];
            });
        }
    }
}
