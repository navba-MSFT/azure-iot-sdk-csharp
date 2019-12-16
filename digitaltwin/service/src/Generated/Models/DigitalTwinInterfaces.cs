// <auto-generated>
// Code generated by Microsoft (R) AutoRest Code Generator.
// Changes may cause incorrect behavior and will be lost if the code is
// regenerated.
// </auto-generated>

namespace Microsoft.Azure.Devices.DigitalTwin.Service.Generated.Models
{
    using Newtonsoft.Json;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    internal partial class DigitalTwinInterfaces
    {
        /// <summary>
        /// Initializes a new instance of the DigitalTwinInterfaces class.
        /// </summary>
        public DigitalTwinInterfaces()
        {
            CustomInit();
        }

        /// <summary>
        /// Initializes a new instance of the DigitalTwinInterfaces class.
        /// </summary>
        /// <param name="interfaces">Interface(s) data on the digital
        /// twin.</param>
        /// <param name="version">Version of digital twin.</param>
        public DigitalTwinInterfaces(IDictionary<string, InterfaceModel> interfaces = default(IDictionary<string, InterfaceModel>), long? version = default(long?))
        {
            Interfaces = interfaces;
            Version = version;
            CustomInit();
        }

        /// <summary>
        /// An initialization method that performs custom operations like setting defaults
        /// </summary>
        partial void CustomInit();

        /// <summary>
        /// Gets or sets interface(s) data on the digital twin.
        /// </summary>
        [JsonProperty(PropertyName = "interfaces")]
        public IDictionary<string, InterfaceModel> Interfaces { get; set; }

        /// <summary>
        /// Gets or sets version of digital twin.
        /// </summary>
        [JsonProperty(PropertyName = "version")]
        public long? Version { get; set; }

    }
}