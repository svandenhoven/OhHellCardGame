﻿using CardGames.Shared;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CardGames.Shared.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using StackExchange.Redis.Extensions.Core.Abstractions;
using System.Text.Json;

namespace CardGames.Server.Services
{
    public class GameService
    {
        private GameSettings _settings;
        private ILogger<GameService> _logger;
        private readonly IRedisCacheClient _redisCacheClient;

        public GameService(IOptions<GameSettings> settings, ILogger<GameService> logger, IRedisCacheClient redisCacheClient)
        {
            _settings = settings.Value;
            _logger = logger;
            _redisCacheClient = redisCacheClient;
        }

        public Game NewGame(string gameId, int nrPlayers)
        {
            _logger.LogInformation($"Adding New Game with id: {gameId}.");
            var game = new Game(gameId, nrPlayers);
            _redisCacheClient.Db0.AddAsync(gameId, game).Wait();

            return game;
        }

        public void SaveGame(Game game)
        {
            _logger.LogInformation($"Saving Game with id: {game.Id}.");
            _redisCacheClient.Db0.RemoveAsync(game.Id);
            _redisCacheClient.Db0.AddAsync(game.Id, game);
        }

        public Game GetGame(string gameId)
        {
            var gameData = _redisCacheClient.Db0.Database.StringGetAsync(gameId).Result;
            var game = JsonSerializer.Deserialize<Game>(gameData);
            return game;
        }

        public Game StartGame(string gameId)
        {
            _logger.LogInformation($"Starting Game with id: {gameId}.");

            var game = GetGame(gameId);
            game.GameStarted = true;
            game.Rounds[0].Current = true;
            SaveGame(game);

            return game;
        }

        public Game ResetGame(string gameId)
        {
            _logger.LogInformation($"Reset Game with id: {gameId}.");
            var nrPlayers = GetGame(gameId).NrPlayers;

            _redisCacheClient.Db0.RemoveAsync(gameId).Wait();
            var game = new Game(gameId, nrPlayers);
            _redisCacheClient.Db0.AddAsync(gameId,game).Wait();

            return game;
        }

        public void RemoveGame(string gameId, string userEmail)
        {
            _logger.LogInformation($"Removing Game with id: {gameId}.");
            if (userEmail == _settings.SystemAdmin)
            {
                _redisCacheClient.Db0.RemoveAsync(gameId).Wait();
            }
        }

        public Game NewGameSet(string gameId)
        {
            _logger.LogInformation($"New Game Set on Game with id: {gameId}.");
            var game = GetGame(gameId);
            game.NewGameSet();
            SaveGame(game);
            return game;
        }

        public List<GamePlayer> GetPlayerGames(string userEmail)
        {
            var gameIds = new List<GamePlayer>();
            var listOfKeys = _redisCacheClient.Db0.SearchKeysAsync("*").Result;
            foreach (var key in listOfKeys)
            {
                var game = GetGame(key);
                foreach (var player in game.Players)
                {
                    var userInGame = new GamePlayer();
                    userInGame.GameId = game.Id;
                    if (player.Email == userEmail || userEmail == _settings.SystemAdmin || IsUserGameAdmin(game.Id, userEmail))
                    {
                        userInGame.Player = player.Id;
                        userInGame.Email = player.Email;
                        userInGame.IsGameAdmin = player.IsGameController;
                        gameIds.Add(userInGame);
                    }                  
                }              
            }
            return gameIds;
        }

        private bool IsUserGameAdmin(string gameId, string playerEmail)
        {
            var game = GetGame(gameId);
            var gamesAdmin = game.Players.Where(p => p.Email == playerEmail && p.IsGameController);
            var isAdmin = gamesAdmin.Count() > 0;
            return isAdmin;
        }
    }
}