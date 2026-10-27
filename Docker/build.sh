# First check your host UID
id -u
# Then build, passing your UID
docker build --build-arg HOST_UID=1000 -t edf-env .
