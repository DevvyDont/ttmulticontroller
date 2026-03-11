using System.Configuration;
using System.Reflection;

namespace TTMulti.Properties
{
    /// <summary>
    /// Partial class that assigns PortableSettingsProvider so user settings are stored
    /// in user.config next to the executable instead of AppData.
    /// </summary>
    internal sealed partial class Settings
    {
        private static readonly PortableSettingsProvider PortableProvider = new PortableSettingsProvider();

        static Settings()
        {
            PortableProvider.Initialize("PortableSettingsProvider", new System.Collections.Specialized.NameValueCollection());

            // Replace the default (AppData) provider with our exe-directory provider.
            object defaultInstance = Default;
            var propsProp = typeof(ApplicationSettingsBase).GetProperty("Properties",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (propsProp?.GetValue(defaultInstance) is SettingsPropertyCollection collection)
            {
                foreach (SettingsProperty prop in collection)
                {
                    if (IsUserScoped(prop))
                        prop.Provider = PortableProvider;
                }
            }
        }

        private static bool IsUserScoped(SettingsProperty property)
        {
            foreach (System.Collections.DictionaryEntry attr in property.Attributes)
            {
                if (attr.Value is UserScopedSettingAttribute)
                    return true;
            }
            return false;
        }
    }
}
