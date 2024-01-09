using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MigrationCreator.Options
{
    internal partial class OptionsProvider
    {
        // Register the options with this attribute on your package class:
        // [ProvideOptionPage(typeof(OptionsProvider.GeneralOptions), "MigrationCreator", "General", 0, 0, true, SupportsProfiles = true)]
        [ComVisible(true)]
        public class GeneralOptions : BaseOptionPage<General> { }
    }

    public class General : BaseOptionModel<General>
    {
        [Category("EServices")]
        [DisplayName("User Name")]
        [Description("An informative description.")]
        [DefaultValue("userName")]
        public string UserName { get; set; } = "userName";
    }
}
