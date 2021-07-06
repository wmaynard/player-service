FROM tomcat:10-jdk16
CMD mkdir -p /usr/local/tomcat/webapps/ROOT/
COPY build/libs/*.war /usr/local/tomcat/webapps/ROOT.war
COPY setenv.sh /usr/local/tomcat/bin/setenv.sh
ENTRYPOINT [ "catalina.sh", "run" ]