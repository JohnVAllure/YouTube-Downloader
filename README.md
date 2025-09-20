GUI for YT-DLP and FFMPEG making it easier to download audio/video files from YouTube. 

<img width="786" height="593" alt="YouTubeDownloader" src="https://github.com/user-attachments/assets/044a4fac-543e-4e42-96c4-1b4a4a2fe949" />


I started this project as a way to make it easier for clippers to download clips for editing.


To build from source yourself: 

1. in the root folder in a terminal run `dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=false`

2. Open installer.iss using Inno Setup Compiler and hit compile > build