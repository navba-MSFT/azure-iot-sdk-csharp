// <auto-generated>
// Code generated by Microsoft (R) AutoRest Code Generator.
// Changes may cause incorrect behavior and will be lost if the code is
// regenerated.
// </auto-generated>

namespace Microsoft.Azure.Devices.DigitalTwin.Service.Models
{
    using Newtonsoft.Json;
    using System.Linq;

    /// <summary>
    /// Defines headers for GetAllInterfaces operation.
    /// </summary>
    internal partial class DigitalTwinGetAllInterfacesHeaders
    {
        /// <summary>
        /// Initializes a new instance of the
        /// DigitalTwinGetAllInterfacesHeaders class.
        /// </summary>
        public DigitalTwinGetAllInterfacesHeaders()
        {
            CustomInit();
        }

        /// <summary>
        /// Initializes a new instance of the
        /// DigitalTwinGetAllInterfacesHeaders class.
        /// </summary>
        /// <param name="eTag">ETag of the digital twin.</param>
        public DigitalTwinGetAllInterfacesHeaders(string eTag = default(string))
        {
            ETag = eTag;
            CustomInit();
        }

        /// <summary>
        /// An initialization method that performs custom operations like setting defaults
        /// </summary>
        partial void CustomInit();

        /// <summary>
        /// Gets or sets eTag of the digital twin.
        /// </summary>
        [JsonProperty(PropertyName = "ETag")]
        public string ETag { get; set; }

    }
}