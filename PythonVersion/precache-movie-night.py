import os
from sys import exit
from pathlib import Path
from http.client import HTTPSConnection
from configparser import ConfigParser
from argparse import ArgumentParser
from urllib.parse import urlparse, urljoin
from urllib.request import urlopen
from json import load


def exitFatal(message):
    print("Error: Failed to precache items")
    print(f"Error: {message}")
    input("Press enter to exit.")
    exit(1)


SERVER_CONFIG_DEFAULT_FILE_NAME: str = "precache-remote-settings.ini"
LOCAL_CONFIG_DEFAULT_FILE_NAME: str = "precache-local-settings.ini"

parser = ArgumentParser(description="Precache Remote Source Tool")
parser.add_argument(
    "server_config",
    help=f"The server source configuration.",
    default=SERVER_CONFIG_DEFAULT_FILE_NAME,
)
parser_result = parser.parse_args()
server_config_path = Path(parser_result.server_config)
if not server_config_path.exists():
    exitFatal(
        f'The server source configuration is required. Default file is "{SERVER_CONFIG_DEFAULT_FILE_NAME}"'
    )

server_config = ConfigParser()
server_config.read(parser_result.server_config)

if not Path(LOCAL_CONFIG_DEFAULT_FILE_NAME).exists():
    print(
        f'Cannot find {LOCAL_CONFIG_DEFAULT_FILE_NAME}, Generating file. Using Default path "MovieNight"'
    )
    local_config = ConfigParser()
    local_config["Application"] = {"DownloadDirectory": ".\\MovieNight"}
    download_directory = local_config["Application"]["DownloadDirectory"]
    with open(LOCAL_CONFIG_DEFAULT_FILE_NAME, "w") as local_config_file:
        local_config.write(local_config_file)
else:
    local_config = ConfigParser()
    local_config.read(LOCAL_CONFIG_DEFAULT_FILE_NAME)

download_directory = local_config.get(
    "Application", "DownloadDirectory", fallback=".\\MovieNight"
)
download_directory_path = Path(download_directory)
if not download_directory_path.exists():
    exitFatal(
        f'The directory "{download_directory}" does not exist. Ensure your plugin is installed and running from the Application Installation Directory or the directory is created.'
    )

playlist_config = server_config.get("Application", "Playlist", fallback=None)
if not playlist_config:
    exitFatal("Playlist is not optional in a Server Configuration .ini!")
parsed_playlist_path = urlparse(playlist_config)
download_server_config = server_config.get(
    "Application", "DownloadServer", fallback=None
)
parsed_download_server = urlparse(download_server_config)
if not parsed_download_server.netloc and not parsed_playlist_path.netloc:
    exitFatal(
        "A fully qualified url is required when DownloadServer is empty for Playlist"
    )
download_server_uri = download_server_config + "/" if download_server_config else ""
playlist_uri = urljoin(download_server_uri, playlist_config)

with urlopen(playlist_uri) as response:
    if response.getcode() != 200:
        exitFatal(f'Unable to find the file at "{playlist_uri}". Unable to continue.')
    playlist_json = load(response)
if not playlist_json or not (
    isinstance(playlist_json, list) and all(isinstance(f, str) for f in playlist_json)
):
    exitFatal(
        "Unexpected Server response after retrieving json! Expected array of strings"
    )

count = 0
for filename in playlist_json:
    file_uri = urljoin(download_server_uri, filename)
    basename = os.path.basename(urlparse(file_uri).path)
    file_path = download_directory_path / basename

    print(f'Downloading "{basename}"')
    with urlopen(file_uri) as response:
        if response.getcode() != 200:
            print("Failed to download file")
            if file_path.exists():
                print(f'Deleting previous pre-cached file "{basename}"')
                file_path.unlink()
            continue

        with open(file_path, "wb") as f:
            while chunk := response.read(4096):
                f.write(chunk)

    print("Complete.")
    count += 1

print(f"Successfully download {count} of {len(playlist_json)} into cache.")
input("Press enter to exit.")
