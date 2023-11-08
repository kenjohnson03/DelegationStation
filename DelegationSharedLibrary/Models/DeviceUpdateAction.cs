using Newtonsoft.Json;

namespace DelegationStationShared.Models
{
    public class DeviceUpdateAction
    {
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }
        public DeviceUpdateActionType ActionType { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }

        public DeviceUpdateAction(Guid id, DeviceUpdateActionType actionType, string name, string value)
        {
            Id = id;
            ActionType = actionType;
            Name = name;
            Value = value;
        }

        public DeviceUpdateAction(DeviceUpdateActionType actionType)
        {
            Id = Guid.NewGuid();
            ActionType = actionType;
            Name = string.Empty;
            Value = string.Empty;
        }

        public DeviceUpdateAction()
        {
            Id = Guid.NewGuid();
            ActionType = DeviceUpdateActionType.Attribute;
            Name = string.Empty;
            Value = string.Empty;
        }
    }

    public enum DeviceUpdateActionType
    {
        Attribute,
        Group,
        AdministrativeUnit
    }
}
