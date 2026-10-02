// Jenkins pipeline for sleepy.
//
// - Lint and Test run for every branch and pull request.
// - Publish and Release run only when Jenkins builds a tag (v0.7.0 and so on):
//   the binaries exist for releases, and building them on every push costs
//   minutes for artifacts nobody downloads.
// - Both desktop builds come from this Linux agent: the Skia desktop head
//   cross-compiles to win-x64. The CLI is Native AOT, which has to be built on
//   the OS it targets, so only its Linux build is produced here.
//
// Requirements on the Jenkins side:
// - the Pipeline, Docker Pipeline and JUnit plugins, and Docker on the agent;
// - for tag builds, a "Secret text" credential with id `github-token` holding a
//   token with contents:write on KopyTKG/Sleepy; and tag discovery enabled in
//   the multibranch / GitHub branch source configuration.
//
// `just ci` runs the same lint, build, test and package steps locally.

pipeline {
    agent {
        docker {
            image 'mcr.microsoft.com/dotnet/sdk:10.0'
            // Root, so the publish stage can install clang and friends.
            args '-u root'
        }
    }

    options {
        timestamps()
        disableConcurrentBuilds(abortPrevious: true)
        buildDiscarder(logRotator(numToKeepStr: '30', artifactNumToKeepStr: '5'))
        timeout(time: 45, unit: 'MINUTES')
    }

    environment {
        DOTNET_NOLOGO = '1'
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        NUGET_PACKAGES = "${WORKSPACE}/.nuget"
        RID = 'linux-x64'
    }

    stages {
        stage('Lint') {
            steps {
                sh 'dotnet format --verify-no-changes'
            }
        }

        stage('Test') {
            steps {
                sh '''
                    dotnet test tests/Sleepy.Core.Tests/Sleepy.Core.Tests.csproj -c Release --nologo \
                        --logger "junit;LogFilePath=$WORKSPACE/test-results.xml"
                '''
            }
            post {
                always {
                    junit allowEmptyResults: false, testResults: 'test-results.xml'
                }
            }
        }

        stage('Publish') {
            when { buildingTag() }
            steps {
                // Native AOT links with clang and needs the zlib headers; zip and
                // jq are for packaging and for the release script.
                sh '''
                    apt-get update -qq
                    apt-get install -y -qq --no-install-recommends clang zlib1g-dev zip jq >/dev/null
                '''

                sh '''
                    rm -rf dist && mkdir -p dist
                    dotnet publish src/Sleepy.Cli/Sleepy.Cli.csproj -c Release -r "$RID" -o dist/cli
                    ./dist/cli/sleepy --version
                    cp dist/cli/sleepy dist/sleepy
                '''

                sh '''
                    dotnet publish src/Sleepy.Gui/Sleepy.Gui.csproj -c Release -f net10.0-desktop \
                        -r linux-x64 --self-contained -o dist/gui-linux-x64
                    tar -czf dist/sleepy-gui-linux-x64.tar.gz -C dist/gui-linux-x64 .
                '''

                sh '''
                    dotnet publish src/Sleepy.Gui/Sleepy.Gui.csproj -c Release -f net10.0-desktop \
                        -r win-x64 --self-contained -o dist/gui-win-x64
                    (cd dist/gui-win-x64 && zip -qr ../sleepy-gui-win-x64.zip .)
                '''

                archiveArtifacts artifacts: 'dist/sleepy, dist/sleepy-gui-linux-x64.tar.gz, dist/sleepy-gui-win-x64.zip',
                    fingerprint: true
            }
        }

        stage('Release') {
            when { buildingTag() }
            steps {
                withCredentials([string(credentialsId: 'github-token', variable: 'GH_TOKEN')]) {
                    sh '''
                        ci/github-release.sh "$TAG_NAME" \
                            dist/sleepy dist/sleepy-gui-linux-x64.tar.gz dist/sleepy-gui-win-x64.zip
                    '''
                }
            }
        }
    }

    post {
        always {
            // The container runs as root; leave nothing the next build cannot delete.
            sh 'chown -R "$(stat -c %u:%g "$WORKSPACE")" "$WORKSPACE" || true'
        }
    }
}
