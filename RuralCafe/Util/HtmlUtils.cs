using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RuralCafe.Util
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Util fields and methods for HTML handling.
    /// </summary>
    public class HtmlUtils
    {
        /// <summary>
        /// HTML attributes that represent links.
        /// </summary>
        public static string[,] LinkTagAttributes = new string[,] {
                  {"a",        "href"}
        };
        /// <summary>
        /// HTML attributes that represent embedded objects.
        /// </summary>
        public static string[,] EmbeddedObjectTagAttributes = new string[,] {
                  //{"base",        "href"},           // 2.0
                  //{"form",        "action"},         // 2.0g
                  {"img",         "src"},            // 2.0
                  {"link",        "href"},           // 2.0

                  {"applet",      "code"},           // 3.2
                  {"applet",      "codebase"},       // 3.2
                  //{"area",        "href"},           // 3.2g
                  {"body",        "background"},     // 3.2
                  //{"img",         "usemap"},         // 3.2g
                  {"input",       "src"},            // 3.2

                  {"applet",      "archive"},        // 4.01
                  {"applet",      "object"},         // 4.01
                  //{"blockquote",  "cite"},           // 4.01g
                  //{"del",         "cite"},           // 4.01g
                  //{"frame",       "longdesc"},       // 4.01g
                  {"frame",       "src"},            // 4.01
                  {"head",        "profile"},        // 4.01
                   //{"iframe",      "longdesc"},       // 4.01g
                  {"iframe",      "src"},            // 4.01
                   //{"img",         "longdesc"},       // 4.01g
                   //{"input",       "usemap"},         // 4.01g
                   //{"ins",         "cite"},           // 4.01g
                  {"object",      "archive"},        // 4.01
                   //{"object",      "classid"},        // 4.01g
                   //{"object",      "codebase"},       // 4.01g
                  {"object",      "data"},           // 4.01
                   //{"object",      "usemap"},         // 4.01g
                   //{"q",           "cite"},           // 4.01g
                  {"script",      "src"},            // 4.01

                  {"audio",       "src"},            // 5.0
                  {"command",     "icon"},           // 5.0
                  {"embed",       "src"},            // 5.0
                  {"event-source","src"},            // 5.0
                  //{"html",        "manifest"},       // 5.0g
                  {"source",      "src"},            // 5.0
                  {"video",       "src"},            // 5.0
                  {"video",       "poster"},         // 5.0

                  {"bgsound",     "src"},            // Extension
                  //{"body",        "credits"},        // Extensiong
                  //{"body",        "instructions"},   // Extensiong
                  //{"body",        "logo"},           // Extensiong
                  //{"div",         "href"},           // Extensiong
                  {"div",         "src"},            // Extension
                  {"embed",       "code"},           // Extension
                  {"embed",       "pluginspage"},    // Extension
                  {"html",        "background"},     // Extension
                  {"ilayer",      "src"},            // Extension
                  {"img",         "dynsrc"},         // Extension
                  {"img",         "lowsrc"},         // Extension
                  {"input",       "dynsrc"},         // Extension
                  {"input",       "lowsrc"},         // Extension
                  {"table",       "background"},     // Extension
                  {"td",          "background"},     // Extension
                  {"th",          "background"},     // Extension
                  {"layer",       "src"},            // Extension
                  {"xml",         "src"},            // Extension

                  //{"button",      "action"},         // Forms 2.0g
                  {"datalist",    "data"},           // Forms 2.0
                  {"form",        "data"},           // Forms 2.0
                  //{"input",       "action"},         // Forms 2.0g
                  {"select",      "data"},           // Forms 2.0

                  {"html",        "xmlns"},

                  //{"access",      "path"},           // 1.3
                  //{"card",        "onenterforward"}, // 1.3
                  //{"card",        "onenterbackward"},// 1.3
                  //{"card",        "ontimer"},        // 1.3
                  //{"go",          "href"},           // 1.3
                  //{"option",      "onpick"},         // 1.3
                  //{"template",    "onenterforward"}, // 1.3
                  //{"template",    "onenterbackward"},// 1.3
                  //{"template",    "ontimer"},        // 1.3
                  {"wml",         "xmlns"}          // 2.0
        };

    }
}