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

        /// <summary>
        /// Override Save so we always persist through our portable provider; the framework often never calls our provider's SetPropertyValues.
        /// </summary>
        public override void Save()
        {
            var propsProp = typeof(ApplicationSettingsBase).GetProperty("Properties",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var props = propsProp?.GetValue(this) as SettingsPropertyCollection;
            if (props != null)
            {
                var values = new SettingsPropertyValueCollection();
                foreach (SettingsProperty prop in props)
                {
                    if (!IsUserScoped(prop)) continue;
                    try
                    {
                        var val = new SettingsPropertyValue(prop);
                        val.SerializedValue = this[prop.Name];
                        val.IsDirty = true;
                        values.Add(val);
                    }
                    catch { }
                }
                if (values.Count > 0)
                    PortableProvider.SetPropertyValues(null, values);
            }
            base.Save();
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
