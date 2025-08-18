using System;

namespace Qobuzarr.Tests.TestData
{
    public static class SampleQobuzResponses
    {
        public const string ValidLoginResponse = @"{
            ""user"": {
                ""id"": 12345678,
                ""email"": ""test@example.com"",
                ""login"": ""test@example.com"",
                ""display_name"": ""Test User"",
                ""firstname"": ""Test"",
                ""lastname"": ""User""
            },
            ""user_auth_token"": ""sample_auth_token_123456"",
            ""label"": ""My Label"",
            ""credential"": {
                ""id"": 12345678,
                ""label"": ""My Label"",
                ""source"": ""qobuz""
            }
        }";

        public const string InvalidLoginResponse = @"{
            ""status"": ""error"",
            ""code"": 400,
            ""message"": ""Invalid credentials""
        }";

        public const string SampleAlbumResponse = @"{
            ""id"": ""0060254788359"",
            ""title"": ""Random Access Memories"",
            ""version"": null,
            ""url"": ""https://www.qobuz.com/album/random-access-memories-daft-punk/0060254788359"",
            ""duration"": 4578,
            ""tracks_count"": 13,
            ""release_date_original"": ""2013-05-17"",
            ""release_date_download"": ""2013-05-20"",
            ""release_date_stream"": ""2013-05-20"",
            ""purchased"": false,
            ""streamable"": true,
            ""downloadable"": false,
            ""displayable"": true,
            ""maximum_bit_depth"": 16,
            ""maximum_sampling_rate"": 44.1,
            ""maximum_channel_count"": 2,
            ""maximum_technical_specifications"": ""16 bits / 44.1 kHz"",
            ""image"": {
                ""small"": ""https://static.qobuz.com/images/covers/59/83/0060254788359_230.jpg"",
                ""thumbnail"": ""https://static.qobuz.com/images/covers/59/83/0060254788359_50.jpg"",
                ""large"": ""https://static.qobuz.com/images/covers/59/83/0060254788359_600.jpg"",
                ""back"": null
            },
            ""artist"": {
                ""id"": 26887,
                ""name"": ""Daft Punk"",
                ""slug"": ""daft-punk"",
                ""albums_count"": 20,
                ""picture"": ""https://static.qobuz.com/images/artists/pictures/ba/58/26887_1424951484_230.jpg""
            },
            ""label"": {
                ""id"": 6842,
                ""name"": ""Columbia"",
                ""slug"": ""columbia"",
                ""albums_count"": 9999
            },
            ""tracks"": {
                ""offset"": 0,
                ""limit"": 50,
                ""total"": 13,
                ""items"": [
                    {
                        ""id"": 23374053,
                        ""position"": 1,
                        ""track_number"": 1,
                        ""media_number"": 1,
                        ""title"": ""Give Life Back to Music"",
                        ""version"": null,
                        ""duration"": 274,
                        ""copyright"": ""℗ 2013 Columbia Records"",
                        ""isrc"": ""USSM11300001"",
                        ""maximum_bit_depth"": 16,
                        ""maximum_sampling_rate"": 44.1,
                        ""maximum_channel_count"": 2,
                        ""streamable"": true,
                        ""performer"": {
                            ""id"": 26887,
                            ""name"": ""Daft Punk""
                        },
                        ""album"": {
                            ""id"": ""0060254788359"",
                            ""title"": ""Random Access Memories""
                        }
                    }
                ]
            }
        }";

        public const string SampleSearchResponse = @"{
            ""albums"": {
                ""limit"": 20,
                ""offset"": 0,
                ""total"": 1,
                ""items"": [
                    {
                        ""id"": ""0060254788359"",
                        ""title"": ""Random Access Memories"",
                        ""version"": null,
                        ""duration"": 4578,
                        ""tracks_count"": 13,
                        ""release_date_original"": ""2013-05-17"",
                        ""streamable"": true,
                        ""downloadable"": false,
                        ""maximum_bit_depth"": 16,
                        ""maximum_sampling_rate"": 44.1,
                        ""image"": {
                            ""small"": ""https://static.qobuz.com/images/covers/59/83/0060254788359_230.jpg"",
                            ""large"": ""https://static.qobuz.com/images/covers/59/83/0060254788359_600.jpg""
                        },
                        ""artist"": {
                            ""id"": 26887,
                            ""name"": ""Daft Punk""
                        }
                    }
                ]
            }
        }";

        public const string StreamUrlResponse = @"{
            ""url"": ""https://streaming.qobuz.com/track/23374053/download.flac?format_id=6&token=sample_token"",
            ""format_id"": 6,
            ""mime_type"": ""audio/flac"",
            ""sampling_rate"": 44.1,
            ""bit_depth"": 16
        }";

        public const string ErrorResponse = @"{
            ""status"": ""error"",
            ""code"": 404,
            ""message"": ""Not found""
        }";

        public const string RateLimitResponse = @"{
            ""status"": ""error"",
            ""code"": 429,
            ""message"": ""Too many requests""
        }";
    }
}