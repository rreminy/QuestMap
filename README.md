# QuestMap
Explore quests and their rewards.
- Search for quest names or their rewards, including instances, beast tribes, minions, etc.
- See an interactive map of quest requirements and unlocks.
- Open a quest info window even for quests you haven't completed.
- Open quest starting locations on the map or open quests in the journal.

## IPC
- `bool QuestMap.ShowGraphByQuestId(uint questId)`: Shows the quest map with the specified quest id selected.
  - **`questId`**: Quest ID to be shown on the quest map.
  - **Returns**: A boolean indicating success. Upon success (`true`) the quest map is shown to the user with the specified quest ID selected. Upon failure (`false`) nothing happens.

- `bool QuestMap.ShowInfoByQuestId(uint questId)`: Shows the quest information for the specified quest id.
  - **`questId`**: Quest ID of the quest information to be shown.
  - **Returns**: A boolean indicating success. Upon success (`true`) the quest information is shown to the user for the specified quest ID. Upon failure (`false`) nothing happens.

## Credits
### Plugin
- Anna for creating this plugin.
- Azure Gem for continued maintenance.

### Icons
- Treasure Map by Anthony Ledoux from the Noun Project
- Locked Book by Anthony Ledoux from the Noun Project
