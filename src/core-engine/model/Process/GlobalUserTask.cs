using HumanInteraction;

namespace Process;

public class GlobalUserTask(string name, string implementation) : GlobalTask(name)
{
    public string Implementation { get; set; } = implementation;

    public List<Rendering> Renderings { get; set; } = [];
}