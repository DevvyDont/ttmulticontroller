using System;
using System.Collections;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Xml;

namespace TTMulti.Properties
{
    /// <summary>
    /// Stores user settings in a config file next to the executable (user.config in the same directory as the exe).
    /// This makes the app portable: settings persist when moving the exe or when updating to a new version in the same folder.
    /// </summary>
    internal sealed class PortableSettingsProvider : SettingsProvider
    {
        private const string UserSettingsGroupName = "userSettings";
        private const string SectionName = "TTMulti.Properties.Settings";
        private static readonly string ConfigFileName = "user.config";

        private string _configPath;

        public override string ApplicationName
        {
            get => "ToontownMulticontroller";
            set { }
        }

        public override void Initialize(string name, System.Collections.Specialized.NameValueCollection config)
        {
            base.Initialize(name, config);
            string exeDir = GetExeDirectory();
            _configPath = Path.Combine(exeDir, ConfigFileName);
            EnsureConfigFileExists();
        }

        /// <summary>
        /// Creates user.config with minimal structure if it doesn't exist, so the file is always present (e.g. fresh copy).
        /// </summary>
        private void EnsureConfigFileExists()
        {
            if (File.Exists(_configPath)) return;
            try
            {
                var doc = new XmlDocument();
                doc.AppendChild(doc.CreateXmlDeclaration("1.0", "utf-8", null));
                doc.AppendChild(doc.CreateElement("configuration"));
                EnsureUserSettingsStructure(doc);
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                doc.Save(_configPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("PortableSettingsProvider EnsureConfigFileExists failed: " + ex.Message);
                try
                {
                    System.Windows.Forms.MessageBox.Show(
                        "Could not create settings file:\n" + _configPath + "\n\n" + ex.Message,
                        "Settings",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                }
                catch { }
            }
        }

        private static string GetExeDirectory()
        {
            try
            {
                string path = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(path))
                    return Path.GetDirectoryName(path);
            }
            catch { }
            try
            {
                string path = System.Windows.Forms.Application.ExecutablePath;
                if (!string.IsNullOrEmpty(path))
                    return Path.GetDirectoryName(path);
            }
            catch { }
            return AppDomain.CurrentDomain.BaseDirectory ?? ".";
        }

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection collection)
        {
            var values = new SettingsPropertyValueCollection();
            XmlDocument doc = LoadConfigDocument();

            foreach (SettingsProperty property in collection)
            {
                var value = new SettingsPropertyValue(property) { IsDirty = false };
                if (IsUserScoped(property))
                {
                    string stored = GetSettingFromXml(doc, property.Name);
                    value.SerializedValue = stored ?? property.DefaultValue;
                }
                else
                {
                    value.SerializedValue = property.DefaultValue;
                }
                values.Add(value);
            }

            // When no config file exists, create it now with current/default values so it always exists.
            if (!File.Exists(_configPath))
            {
                foreach (SettingsProperty property in collection)
                {
                    if (!IsUserScoped(property)) continue;
                    object val = GetSettingFromXml(doc, property.Name) ?? property.DefaultValue;
                    SetSettingInXml(doc, property.Name, val, property.SerializeAs);
                }
                SaveConfigDocument(doc);
            }
            return values;
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection collection)
        {
            XmlDocument doc = LoadConfigDocument();
            bool changed = false;
            bool fileMissing = !File.Exists(_configPath);
            foreach (SettingsPropertyValue value in collection)
            {
                if (!IsUserScoped(value.Property))
                    continue;
                if (value.IsDirty)
                {
                    SetSettingInXml(doc, value.Property.Name, value.SerializedValue, value.Property.SerializeAs);
                    changed = true;
                }
                else if (fileMissing)
                {
                    // When config file doesn't exist yet, persist all current values so the file gets created.
                    SetSettingInXml(doc, value.Property.Name, value.SerializedValue, value.Property.SerializeAs);
                    changed = true;
                }
            }
            if (changed)
                SaveConfigDocument(doc);
        }

        private static bool IsUserScoped(SettingsProperty property)
        {
            foreach (DictionaryEntry attr in property.Attributes)
            {
                if (attr.Value is UserScopedSettingAttribute)
                    return true;
            }
            return false;
        }

        private XmlDocument LoadConfigDocument()
        {
            var doc = new XmlDocument();
            try
            {
                if (File.Exists(_configPath))
                {
                    doc.Load(_configPath);
                }
                else
                {
                    doc.AppendChild(doc.CreateXmlDeclaration("1.0", "utf-8", null));
                    doc.AppendChild(doc.CreateElement("configuration"));
                }
            }
            catch
            {
                doc.RemoveAll();
                doc.AppendChild(doc.CreateXmlDeclaration("1.0", "utf-8", null));
                doc.AppendChild(doc.CreateElement("configuration"));
            }
            EnsureUserSettingsStructure(doc);
            return doc;
        }

        private void EnsureUserSettingsStructure(XmlDocument doc)
        {
            XmlElement root = doc.DocumentElement;
            if (root == null) return;
            XmlElement userSettings = root["userSettings"];
            if (userSettings == null)
            {
                userSettings = doc.CreateElement(UserSettingsGroupName);
                root.AppendChild(userSettings);
            }
            XmlElement section = userSettings[SectionName];
            if (section == null)
            {
                section = doc.CreateElement(SectionName);
                userSettings.AppendChild(section);
            }
        }

        private string GetSettingFromXml(XmlDocument doc, string name)
        {
            XmlElement section = doc.DocumentElement?[UserSettingsGroupName]?[SectionName];
            if (section == null) return null;
            foreach (XmlElement setting in section.SelectNodes("setting"))
            {
                if (setting.GetAttribute("name") == name)
                {
                    XmlNode valueNode = setting.SelectSingleNode("value");
                    return valueNode?.InnerText;
                }
            }
            return null;
        }

        private void SetSettingInXml(XmlDocument doc, string name, object value, SettingsSerializeAs serializeAs)
        {
            EnsureUserSettingsStructure(doc);
            XmlElement section = doc.DocumentElement?[UserSettingsGroupName]?[SectionName];
            if (section == null) return;
            XmlElement settingEl = null;
            foreach (XmlElement el in section.SelectNodes("setting"))
            {
                if (el.GetAttribute("name") == name)
                {
                    settingEl = el;
                    break;
                }
            }
            string valueStr = value == null ? string.Empty : (value is string s ? s : value.ToString());

            if (settingEl == null)
            {
                settingEl = doc.CreateElement("setting");
                settingEl.SetAttribute("name", name);
                settingEl.SetAttribute("serializeAs", serializeAs.ToString());
                XmlElement valueEl = doc.CreateElement("value");
                valueEl.InnerText = valueStr;
                settingEl.AppendChild(valueEl);
                section.AppendChild(settingEl);
            }
            else
            {
                var valueNode = settingEl.SelectSingleNode("value");
                if (valueNode != null)
                    valueNode.InnerText = valueStr;
                else
                {
                    XmlElement valueEl = doc.CreateElement("value");
                    valueEl.InnerText = valueStr;
                    settingEl.AppendChild(valueEl);
                }
            }
        }

        private void SaveConfigDocument(XmlDocument doc)
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                doc.Save(_configPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("PortableSettingsProvider Save failed: " + ex.Message);
                try
                {
                    System.Windows.Forms.MessageBox.Show(
                        "Settings could not be saved to:\n" + _configPath + "\n\n" + ex.Message + "\n\nCheck that the folder is writable (e.g. not run from a read-only location).",
                        "Settings Save Failed",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                }
                catch { }
            }
        }
    }
}
