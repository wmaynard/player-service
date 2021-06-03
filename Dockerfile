FROM tomcat:8.5-jdk8
CMD mkdir -p /usr/local/tomcat/webapps/ROOT/
COPY build/libs/*.war /usr/local/tomcat/webapps/ROOT.war

