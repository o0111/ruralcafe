﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.18047
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace RuralCafe.Properties {
    
    
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "11.0.0.0")]
    internal sealed partial class Settings : global::System.Configuration.ApplicationSettingsBase {
        
        private static Settings defaultInstance = ((Settings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));
        
        public static Settings Default {
            get {
                return defaultInstance;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("127.0.0.1")]
        public string LOCAL_PROXY_IP_ADDRESS {
            get {
                return ((string)(this["LOCAL_PROXY_IP_ADDRESS"]));
            }
            set {
                this["LOCAL_PROXY_IP_ADDRESS"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("8080")]
        public int LOCAL_PROXY_LISTEN_PORT {
            get {
                return ((int)(this["LOCAL_PROXY_LISTEN_PORT"]));
            }
            set {
                this["LOCAL_PROXY_LISTEN_PORT"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("127.0.0.1")]
        public string REMOTE_PROXY_IP_ADDRESS {
            get {
                return ((string)(this["REMOTE_PROXY_IP_ADDRESS"]));
            }
            set {
                this["REMOTE_PROXY_IP_ADDRESS"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("8081")]
        public int REMOTE_PROXY_LISTEN_PORT {
            get {
                return ((int)(this["REMOTE_PROXY_LISTEN_PORT"]));
            }
            set {
                this["REMOTE_PROXY_LISTEN_PORT"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("Slow")]
        public global::RuralCafe.RCLocalProxy.NetworkStatusCode NETWORK_STATUS {
            get {
                return ((global::RuralCafe.RCLocalProxy.NetworkStatusCode)(this["NETWORK_STATUS"]));
            }
            set {
                this["NETWORK_STATUS"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string EXTERNAL_PROXY_IP_ADDRESS {
            get {
                return ((string)(this["EXTERNAL_PROXY_IP_ADDRESS"]));
            }
            set {
                this["EXTERNAL_PROXY_IP_ADDRESS"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("0")]
        public int EXTERNAL_PROXY_LISTEN_PORT {
            get {
                return ((int)(this["EXTERNAL_PROXY_LISTEN_PORT"]));
            }
            set {
                this["EXTERNAL_PROXY_LISTEN_PORT"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string EXTERNAL_PROXY_LOGIN {
            get {
                return ((string)(this["EXTERNAL_PROXY_LOGIN"]));
            }
            set {
                this["EXTERNAL_PROXY_LOGIN"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string EXTERNAL_PROXY_PASS {
            get {
                return ((string)(this["EXTERNAL_PROXY_PASS"]));
            }
            set {
                this["EXTERNAL_PROXY_PASS"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("trotro.html")]
        public string DEFAULT_SEARCH_PAGE {
            get {
                return ((string)(this["DEFAULT_SEARCH_PAGE"]));
            }
            set {
                this["DEFAULT_SEARCH_PAGE"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("2000000")]
        public int DEFAULT_QUOTA {
            get {
                return ((int)(this["DEFAULT_QUOTA"]));
            }
            set {
                this["DEFAULT_QUOTA"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1")]
        public int DEFAULT_DEPTH {
            get {
                return ((int)(this["DEFAULT_DEPTH"]));
            }
            set {
                this["DEFAULT_DEPTH"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("Normal")]
        public global::RuralCafe.RequestHandler.Richness DEFAULT_RICHNESS {
            get {
                return ((global::RuralCafe.RequestHandler.Richness)(this["DEFAULT_RICHNESS"]));
            }
            set {
                this["DEFAULT_RICHNESS"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("5000000")]
        public int MAXIMUM_DOWNLOAD_SPEED {
            get {
                return ((int)(this["MAXIMUM_DOWNLOAD_SPEED"]));
            }
            set {
                this["MAXIMUM_DOWNLOAD_SPEED"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("index-computer_studies-test")]
        public string INDEX_PATH {
            get {
                return ((string)(this["INDEX_PATH"]));
            }
            set {
                this["INDEX_PATH"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("files-computer_studies-test")]
        public string LOCAL_CACHE_PATH {
            get {
                return ((string)(this["LOCAL_CACHE_PATH"]));
            }
            set {
                this["LOCAL_CACHE_PATH"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("Cache")]
        public string REMOTE_CACHE_PATH {
            get {
                return ((string)(this["REMOTE_CACHE_PATH"]));
            }
            set {
                this["REMOTE_CACHE_PATH"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string WIKI_DUMP_DIR {
            get {
                return ((string)(this["WIKI_DUMP_DIR"]));
            }
            set {
                this["WIKI_DUMP_DIR"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string WIKI_DUMP_FILE {
            get {
                return ((string)(this["WIKI_DUMP_FILE"]));
            }
            set {
                this["WIKI_DUMP_FILE"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("50")]
        public int LOCAL_MAXIMUM_ACTIVE_REQUESTS {
            get {
                return ((int)(this["LOCAL_MAXIMUM_ACTIVE_REQUESTS"]));
            }
            set {
                this["LOCAL_MAXIMUM_ACTIVE_REQUESTS"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("DEBUG")]
        public global::RuralCafe.LogLevel LOGLEVEL {
            get {
                return ((global::RuralCafe.LogLevel)(this["LOGLEVEL"]));
            }
            set {
                this["LOGLEVEL"] = value;
            }
        }
    }
}
