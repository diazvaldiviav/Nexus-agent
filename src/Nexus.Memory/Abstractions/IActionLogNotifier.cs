using Nexus.Memory.Models;

namespace Nexus.Memory.Abstractions;

public interface IActionLogNotifier
{
    event Action<AgentAction>? ActionLogged;
}
