﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.18052
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace RuralCafe.Properties {
    
    
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "11.0.0.0")]
    internal sealed partial class Connection : global::System.Configuration.ApplicationSettingsBase {
        
        private static Connection defaultInstance = ((Connection)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Connection())));
        
        public static Connection Default {
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
        [global::System.Configuration.DefaultSettingValueAttribute("8443")]
        public int LOCAL_PROXY_HTTPS_PORT {
            get {
                return ((int)(this["LOCAL_PROXY_HTTPS_PORT"]));
            }
            set {
                this["LOCAL_PROXY_HTTPS_PORT"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("8444")]
        public int REMOTE_PROXY_HTTPS_PORT {
            get {
                return ((int)(this["REMOTE_PROXY_HTTPS_PORT"]));
            }
            set {
                this["REMOTE_PROXY_HTTPS_PORT"] = value;
            }
        }
    }
}