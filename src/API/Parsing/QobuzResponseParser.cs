using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.API.Parsing
{
    /// <summary>
    /// Implementation of response parsing for Qobuz API responses.
    /// Handles JSON deserialization, validation, and error response parsing.
    /// </summary>
    public class QobuzResponseParser : IQobuzResponseParser
    {
        private readonly Logger _logger;
        private readonly JsonSerializerSettings _serializerSettings;

        public QobuzResponseParser(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serializerSettings = CreateSerializerSettings();
        }

        /// <inheritdoc/>
        public T ParseResponse<T>(string content) where T : class
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new QobuzResponseParsingException(
                    "Response content is empty or null",
                    content ?? string.Empty,
                    typeof(T));
            }

            try
            {
                _logger.Trace("Parsing response to type {0}, content length: {1}", typeof(T).Name, content.Length);

                // Log first 500 chars for debugging (sanitized)
                if (content.Length > 0)
                {
                    var preview = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
                    _logger.Trace("Response preview: {0}", preview);
                }

                var result = JsonConvert.DeserializeObject<T>(content, _serializerSettings);

                if (result == null)
                {
                    throw new QobuzResponseParsingException(
                        $"Deserialization returned null for type {typeof(T).Name}",
                        content,
                        typeof(T));
                }

                // Validate the parsed response
                if (!ValidateResponse(result))
                {
                    _logger.Warn("Response validation failed for type {0}", typeof(T).Name);
                }

                _logger.Debug("Successfully parsed response to {0}", typeof(T).Name);
                return result;
            }
            catch (JsonException ex)
            {
                _logger.Error(ex, "JSON parsing failed for type {0}", typeof(T).Name);
                
                // Try to extract error information if this is an error response
                var errorResponse = TryParseErrorResponse(content);
                if (errorResponse != null)
                {
                    throw new QobuzResponseParsingException(
                        $"API returned error: {errorResponse.Message ?? "Unknown error"}",
                        content,
                        typeof(T),
                        ex);
                }

                throw new QobuzResponseParsingException(
                    $"Failed to parse response as {typeof(T).Name}: {ex.Message}",
                    content,
                    typeof(T),
                    ex);
            }
            catch (Exception ex) when (!(ex is QobuzResponseParsingException))
            {
                _logger.Error(ex, "Unexpected error parsing response for type {0}", typeof(T).Name);
                throw new QobuzResponseParsingException(
                    $"Unexpected error parsing response: {ex.Message}",
                    content,
                    typeof(T),
                    ex);
            }
        }

        /// <inheritdoc/>
        public QobuzErrorResponse? TryParseErrorResponse(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<QobuzErrorResponse>(content, _serializerSettings);
            }
            catch (JsonException ex)
            {
                _logger.Trace(ex, "Content is not a valid error response");
                return null;
            }
        }

        /// <inheritdoc/>
        public bool ValidateResponse<T>(T response) where T : class
        {
            if (response == null)
                return false;

            // Type-specific validation
            switch (response)
            {
                case QobuzAlbum album:
                    return ValidateAlbum(album);
                    
                case QobuzTrack track:
                    return ValidateTrack(track);
                    
                case QobuzArtist artist:
                    return ValidateArtist(artist);
                    
                case QobuzPlaylist playlist:
                    return ValidatePlaylist(playlist);
                    
                case QobuzStreamResponse streamResponse:
                    return ValidateStreamResponse(streamResponse);
                    
                case QobuzSearchResponse searchResponse:
                    return ValidateSearchResponse(searchResponse);
                    
                default:
                    // For unknown types, just check it's not null
                    return true;
            }
        }

        /// <inheritdoc/>
        public JsonSerializerSettings GetSerializerSettings()
        {
            return _serializerSettings;
        }

        private JsonSerializerSettings CreateSerializerSettings()
        {
            return new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new SnakeCaseNamingStrategy()
                },
                Converters = new List<JsonConverter>
                {
                    new StringEnumConverter(),
                    new IsoDateTimeConverter { DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffK" }
                },
                Error = (sender, args) =>
                {
                    _logger.Trace("JSON deserialization error at path {0}: {1}", 
                        args.ErrorContext.Path, 
                        args.ErrorContext.Error.Message);
                    
                    // Mark as handled to continue parsing
                    args.ErrorContext.Handled = true;
                }
            };
        }

        private bool ValidateAlbum(QobuzAlbum album)
        {
            if (string.IsNullOrWhiteSpace(album.Id))
            {
                _logger.Warn("Album validation failed: missing ID");
                return false;
            }

            if (string.IsNullOrWhiteSpace(album.Title))
            {
                _logger.Warn("Album validation failed: missing title for ID {0}", album.Id);
                return false;
            }

            return true;
        }

        private bool ValidateTrack(QobuzTrack track)
        {
            if (track.Id <= 0)
            {
                _logger.Warn("Track validation failed: invalid ID");
                return false;
            }

            if (string.IsNullOrWhiteSpace(track.Title))
            {
                _logger.Warn("Track validation failed: missing title for ID {0}", track.Id);
                return false;
            }

            return true;
        }

        private bool ValidateArtist(QobuzArtist artist)
        {
            if (artist.Id <= 0)
            {
                _logger.Warn("Artist validation failed: invalid ID");
                return false;
            }

            if (string.IsNullOrWhiteSpace(artist.Name))
            {
                _logger.Warn("Artist validation failed: missing name for ID {0}", artist.Id);
                return false;
            }

            return true;
        }

        private bool ValidatePlaylist(QobuzPlaylist playlist)
        {
            if (playlist.Id <= 0)
            {
                _logger.Warn("Playlist validation failed: invalid ID");
                return false;
            }

            if (string.IsNullOrWhiteSpace(playlist.Name))
            {
                _logger.Warn("Playlist validation failed: missing name for ID {0}", playlist.Id);
                return false;
            }

            return true;
        }

        private bool ValidateStreamResponse(QobuzStreamResponse streamResponse)
        {
            if (string.IsNullOrWhiteSpace(streamResponse.Url))
            {
                _logger.Warn("Stream response validation failed: missing URL");
                return false;
            }

            return true;
        }

        private bool ValidateSearchResponse(QobuzSearchResponse searchResponse)
        {
            // Search response can be empty, that's valid
            return true;
        }
    }
}