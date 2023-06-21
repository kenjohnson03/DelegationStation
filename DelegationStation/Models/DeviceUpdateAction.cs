using Newtonsoft.Json;

namespace DelegationStation.Models
{
    public class DeviceUpdateAction
    {
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; } = Guid.NewGuid();
        public DeviceUpdateActionType ActionType { get; set; } = DeviceUpdateActionType.Attribute;
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;

        public DeviceUpdateAction(Guid id, DeviceUpdateActionType actionType, string name, string value)
        {
            Id = id;
            ActionType = actionType;
            Name = name;
            Value = value;
        }
    }

    public enum DeviceUpdateActionType
    {
        Attribute,
        Group,
        AdministrativeUnit
    }
}
