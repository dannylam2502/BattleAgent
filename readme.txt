1. To open the project, please use Unity 2022.3.38f1.
Look for GameScene under Assets/Scenes folder
All configurable options are in: Assets/Config
- GameConfig_1: Set up the number of each agent for both side
- PlayerProperties/EnemiesProperties: Stats for each type of agent
- Actions/Action: All the action that's currently in the game.

To change all available actions for each agent, go to Prefabs/pf_Player or pf_Enemy, look for Available Actions, it will take an array of Actions


2. Design choices:
- Objects: Battle System, Agent, Actions.
- Use polymorphism to extent Agent to Player and Enemy class, same for Actions.
- Each action has their own "function" for the effect.
- Each agent has their default properties, and their properties will be maintained throughout the game.
- Use MVC framework to separate game logic and UIs

3. Record of Development process:
Q1: 15m
Q2: 1h
Q3: 1.5h
Q4 + Polish + Fix bugs: 1h
Please check out Git log for more detail